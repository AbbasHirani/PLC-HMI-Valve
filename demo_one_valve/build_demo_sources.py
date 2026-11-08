# Generates the three PLC sources for the one-valve client demo (project valveDemo_OneValve).
# Run from the repo root. Reads the real project's sources and writes demo_*.xml beside this file,
# so the real sources are never edited. Re-run it if the real blocks change.
import os
import re
import sys
import xml.dom.minidom as minidom

HERE = os.path.dirname(os.path.abspath(__file__))


def read(name):
    return open(name, encoding='utf-8').read()


def write(name, text):
    path = os.path.join(HERE, name)
    open(path, 'w', encoding='utf-8', newline='').write(text)
    minidom.parse(path)
    print('wrote', name)


# ---------- FC_PhysicalIoCopy: one valve on the CPU's own I/O ----------
BODY = '''    // ONE-VALVE DEMO ONLY. The real block copies 368 inputs and 208 outputs from the ET200SP
    // stations. This demo has no stations, so only CM79 (slot 21) is connected, and it uses the
    // CPU's own I/O. FC_IoMapper and FB_ValveLoop are unchanged and still use channels 81-84, 41-42.
    &quot;IO_Buffer_DB&quot;.DI[81] := %I0.0;   // Open limit switch
    &quot;IO_Buffer_DB&quot;.DI[82] := %I0.1;   // Closed limit switch
    &quot;IO_Buffer_DB&quot;.DI[83] := %I0.3;   // Healthy (0.2 left free: wiring room)
    &quot;IO_Buffer_DB&quot;.DI[84] := %I0.4;   // Local mode
    %Q0.0 := &quot;IO_Buffer_DB&quot;.DQ[41];   // OPEN command
    %Q0.1 := &quot;IO_Buffer_DB&quot;.DQ[42];   // CLOSE command
'''
fc = read('temp_fc_physical_io_copy.xml')
fc, n = re.subn(r'(<Text UId="1">\n).*?(\s*</Text>)', lambda m: m.group(1) + BODY + m.group(2), fc, count=1, flags=re.S)
if n != 1:
    sys.exit('FC_PhysicalIoCopy body not found')
write('demo_fc_physical_io_copy.xml', fc)

# ---------- Valves_DB: CM79 in service at power-up, screen fixed on valve 21 ----------
db = read('temp_valves_db.xml')
old = '<Member Name="SelIdx" Datatype="Int" Remanence="NonRetain" Accessibility="Public" />'
if db.count(old) != 1:
    sys.exit('SelIdx member not found')
db = db.replace(old, old[:-3] + '>\n      <StartValue>21</StartValue>\n    </Member>', 1)
db, n = re.subn(r'(<Member Name="ConfiguredRet".*?)(\s*</Member>)',
                lambda m: m.group(1) + '\n      <Subelement Path="21">\n        <StartValue>true</StartValue>\n      </Subelement>' + m.group(2),
                db, count=1, flags=re.S)
if n != 1:
    sys.exit('ConfiguredRet member not found')
write('demo_valves_db.xml', db)

# ---------- Valve_Channels_DB: shorter travel timeout on CM79 ----------
ch = read('temp_valve_channels_db.xml')
ch, n = re.subn(r'(<Member Name="TravelTimeout".*?<Subelement Path="21">\s*<StartValue>)T#60s(</StartValue>)',
                lambda m: m.group(1) + 'T#20s' + m.group(2), ch, count=1, flags=re.S)
if n != 1:
    sys.exit('TravelTimeout[21] not found')
write('demo_valve_channels_db.xml', ch)
