import re, sys, os
import xml.dom.minidom as minidom

SC = os.path.dirname(os.path.abspath(__file__))


def esc(s):
    return s.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


W = lambda n: '"Diag_DB".W_ModuleFault[%d]' % n

# (description, Laddr, IoState, W0, W1, W2, ModuleFault, UnknownModuleFault)
# Expected values are written out by hand from the module table, NOT computed with the FC's own
# formula, so a mistake in that formula cannot pass by agreeing with itself.
TESTS = [
    ('AFT slot 3 fault',                     283,  '0011', '0004', '0000', '0000', True,  False),
    ('AFT slot 8 fault, slot 3 stays',       281,  '0011', '0084', '0000', '0000', True,  False),
    ('AFT slot 3 clears, slot 8 stays',      283,  '0001', '0080', '0000', '0000', True,  False),
    ('AFT slot 8 clears, summary drops',     281,  '0001', '0000', '0000', '0000', False, False),
    ('FWD slot 14 fault',                    326,  '0011', '0000', '0000', '0008', True,  False),
    ('Bit 0 = 0 must NOT clear FWD slot 14', 326,  '0000', '0000', '0000', '0008', True,  False),
    ('Unknown module fault',                 9999, '0011', '0000', '0000', '0018', True,  True),
    ('Healthy unknown must NOT clear it',    9999, '0001', '0000', '0000', '0018', True,  True),
    # Added beyond the eight manual tests: the other "must not clear" bits, and word boundaries.
    ('FWD slot 14 real clear, unknown stays', 326, '0001', '0000', '0000', '0010', True,  True),
    ('MID slot 5 is word 0 bit 15',          299,  '0011', '8000', '0000', '0010', True,  True),
    ('Bit 5 = 1 must NOT clear MID slot 5',  299,  '0021', '8000', '0000', '0010', True,  True),
    ('Bit 7 = 1 must NOT clear MID slot 5',  299,  '0081', '8000', '0000', '0010', True,  True),
    ('MID slot 6 is word 1 bit 0',           300,  '0011', '8000', '0001', '0010', True,  True),
    ('FWD slot 11 is word 2 bit 0',          323,  '0011', '8000', '0001', '0011', True,  True),
]

body = ['''// TEMPORARY. Runs in PLCSIM only, then this block, SelfTest_DB and the call in FB_ValveLoop are
// removed. Drives FC_ModuleEvent through a fixed sequence in one call and counts the results.
// It starts and finishes by zeroing the module fault data, so alarms do not flash up.
"Diag_DB".W_ModuleFault[0] := W#16#0;
"Diag_DB".W_ModuleFault[1] := W#16#0;
"Diag_DB".W_ModuleFault[2] := W#16#0;
"Diag_DB".ModuleFault := FALSE;
"Diag_DB".UnknownModuleFault := FALSE;
"SelfTest_DB".Passed := 0;
"SelfTest_DB".FirstFailedTest := 0;
"SelfTest_DB".Done := FALSE;
"SelfTest_DB".Total := %d;
''' % len(TESTS)]

for n, (desc, laddr, state, w0, w1, w2, mf, unk) in enumerate(TESTS, 1):
    body.append('''
// Test %d: %s
"FC_ModuleEvent"(Laddr := %d, IoState := W#16#%s);
#ok := %s = W#16#%s AND %s = W#16#%s AND %s = W#16#%s
       AND "Diag_DB".ModuleFault = %s AND "Diag_DB".UnknownModuleFault = %s;
IF #ok THEN
    "SelfTest_DB".Passed := "SelfTest_DB".Passed + 1;
ELSIF "SelfTest_DB".FirstFailedTest = 0 THEN
    "SelfTest_DB".FirstFailedTest := %d;
END_IF;
''' % (n, desc, laddr, state, W(0), w0, W(1), w1, W(2), w2, str(mf).upper(), str(unk).upper(), n))

body.append('''
"Diag_DB".W_ModuleFault[0] := W#16#0;
"Diag_DB".W_ModuleFault[1] := W#16#0;
"Diag_DB".W_ModuleFault[2] := W#16#0;
"Diag_DB".ModuleFault := FALSE;
"Diag_DB".UnknownModuleFault := FALSE;
"SelfTest_DB".Done := TRUE;
"SelfTest_DB".RunModuleSelfTest := FALSE;
''')
BODY = ''.join(body)

# ---------- FC, on the FC_ModuleEvent wrapper ----------
f = open('temp_fc_module_event.xml', encoding='utf-8').read()
iface = ('<Interface><Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">\n'
         '  <Section Name="Input" />\n  <Section Name="Output" />\n  <Section Name="InOut" />\n'
         '  <Section Name="Temp">\n    <Member Name="ok" Datatype="Bool" />\n  </Section>\n'
         '  <Section Name="Constant" />\n</Sections></Interface>')
f, a = re.subn(r'<Interface>.*?</Interface>', lambda m: iface, f, count=1, flags=re.S)
f, b = re.subn(r'<Name>FC_ModuleEvent</Name>', '<Name>FC_ModuleSelfTest</Name>', f)
f, c = re.subn(r'(<Text UId="1">).*?(</Text>)', lambda m: m.group(1) + '\n' + esc(BODY) + '  ' + m.group(2), f, count=1, flags=re.S)
if (a, b, c) != (1, 1, 1):
    sys.exit('FC wrapper mismatch %s' % str((a, b, c)))
open(os.path.join(SC, 'selftest_fc.xml'), 'w', encoding='utf-8', newline='').write(f)


# ---------- DB, on the Diag_DB wrapper ----------
def member(n, t):
    return ('    <Member Name="%s" Datatype="%s" Remanence="NonRetain" Accessibility="Public">\n      <AttributeList>\n'
            '        <BooleanAttribute Name="ExternalAccessible" SystemDefined="true">true</BooleanAttribute>\n'
            '        <BooleanAttribute Name="ExternalVisible" SystemDefined="true">true</BooleanAttribute>\n'
            '        <BooleanAttribute Name="ExternalWritable" SystemDefined="true">true</BooleanAttribute>\n'
            '      </AttributeList>\n    </Member>\n') % (n, t)


d = open('temp_diag_db.xml', encoding='utf-8').read()
members = ''.join(member(n, t) for n, t in (
    ('RunModuleSelfTest', 'Bool'), ('Done', 'Bool'), ('Passed', 'Int'), ('Total', 'Int'), ('FirstFailedTest', 'Int')))
iface = ('<Interface><Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">\n'
         '  <Section Name="Static">\n' + members + '  </Section>\n</Sections></Interface>')
d, a = re.subn(r'<Interface>.*?</Interface>', lambda m: iface, d, count=1, flags=re.S)
d, b = re.subn(r'<Name>Diag_DB</Name>', '<Name>SelfTest_DB</Name>', d)
d, c = re.subn(r'(<Culture>en-US</Culture>\s*<Text>).*?(</Text>)',
               lambda m: m.group(1) + 'TEMPORARY results of FC_ModuleSelfTest. Removed once the test has passed.' + m.group(2),
               d, count=1, flags=re.S)
if (a, b, c) != (1, 1, 1):
    sys.exit('DB wrapper mismatch %s' % str((a, b, c)))
open(os.path.join(SC, 'selftest_db.xml'), 'w', encoding='utf-8', newline='').write(d)

# ---------- FB_ValveLoop COPY with the call line. The repo source stays clean. ----------
Q = '&quot;'
fb = open('temp_fb_valveloop.xml', encoding='utf-8').read()
anchor = '    // I/O module diagnostics. OB82 calls FC_ModuleEvent directly'
if fb.count(anchor) != 1:
    sys.exit('FB anchor')
call = ('    // TEMPORARY self-test call. The repo source does not contain this.\n'
        '    IF ' + Q + 'SelfTest_DB' + Q + '.RunModuleSelfTest THEN\n'
        '        ' + Q + 'FC_ModuleSelfTest' + Q + '();\n'
        '    END_IF;\n\n')
open(os.path.join(SC, 'selftest_fb.xml'), 'w', encoding='utf-8', newline='').write(fb.replace(anchor, call + anchor, 1))

for n in ('selftest_fc.xml', 'selftest_db.xml', 'selftest_fb.xml'):
    minidom.parse(os.path.join(SC, n))
print(len(TESTS), 'tests generated')
