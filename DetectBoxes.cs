using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Scans an HMI illustration PNG for the grey valve-symbol boxes and prints each one's
// bounding box + centre in image pixel coordinates. The illustrations are authored at the
// same 1888px design width the screens use, so these coordinates drop straight into the
// screen builder's overlay table with no rescaling.
class DetectBoxes
{
    static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : @"C:\hmi_graphics\AFT zone1.png";
        bool histogram = args.Length > 1 && args[1] == "--hist";

        using (Bitmap bmp = new Bitmap(path))
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = bd.Stride;
            byte[] px = new byte[stride * h];
            Marshal.Copy(bd.Scan0, px, 0, px.Length);
            bmp.UnlockBits(bd);

            if (histogram)
            {
                // Which greys actually occur, so the fill colour isn't guessed.
                var counts = new Dictionary<int, int>();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int o = y * stride + x * 4;
                        int b = px[o], g = px[o + 1], r = px[o + 2];
                        if (Math.Abs(r - g) < 8 && Math.Abs(g - b) < 8 && r > 60 && r < 210)
                        {
                            int key = r / 5 * 5;
                            if (!counts.ContainsKey(key)) counts[key] = 0;
                            counts[key]++;
                        }
                    }
                var keys = new List<int>(counts.Keys);
                keys.Sort((a, b2) => counts[b2].CompareTo(counts[a]));
                Console.WriteLine("grey_level  pixel_count");
                for (int i = 0; i < Math.Min(12, keys.Count); i++)
                    Console.WriteLine("{0,9}  {1,10}", keys[i], counts[keys[i]]);
                return;
            }

            // Grey box fill: near-neutral, mid-tone. Excludes the black outlines, the white
            // ground, the cyan tanks and the yellow labels.
            bool[] mask = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = y * stride + x * 4;
                    int b = px[o], g = px[o + 1], r = px[o + 2], a = px[o + 3];
                    if (a > 128 && Math.Abs(r - g) < 12 && Math.Abs(g - b) < 12
                        && r >= 110 && r <= 175)
                        mask[y * w + x] = true;
                }

            // Connected components, 4-neighbour BFS.
            int[] label = new int[w * h];
            int next = 0;
            var boxes = new List<int[]>();   // minX, minY, maxX, maxY, area
            var queue = new Queue<int>();
            for (int start = 0; start < w * h; start++)
            {
                if (!mask[start] || label[start] != 0) continue;
                next++;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, area = 0;
                queue.Enqueue(start);
                label[start] = next;
                while (queue.Count > 0)
                {
                    int p = queue.Dequeue();
                    int x = p % w, y = p / w;
                    area++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    if (x > 0     && mask[p - 1] && label[p - 1] == 0) { label[p - 1] = next; queue.Enqueue(p - 1); }
                    if (x < w - 1 && mask[p + 1] && label[p + 1] == 0) { label[p + 1] = next; queue.Enqueue(p + 1); }
                    if (y > 0     && mask[p - w] && label[p - w] == 0) { label[p - w] = next; queue.Enqueue(p - w); }
                    if (y < h - 1 && mask[p + w] && label[p + w] == 0) { label[p + w] = next; queue.Enqueue(p + w); }
                }
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                // Valve symbols are small filled squares. Reject text antialiasing (tiny),
                // long thin runs, and any large grey region.
                if (area >= 150 && bw >= 12 && bw <= 70 && bh >= 12 && bh <= 70
                    && Math.Abs(bw - bh) <= 14 && area > bw * bh * 0.55)
                    boxes.Add(new int[] { minX, minY, maxX, maxY, area });
                else if (area >= 150)
                    // Anything grey and big enough to be a valve symbol but rejected by the shape
                    // filter — almost always two adjacent boxes merged into one blob, which would
                    // silently undercount. Printed so the count can be trusted.
                    Console.WriteLine("# REJECTED  area=" + area + "  w=" + bw + " h=" + bh
                        + "  at " + minX + "," + minY);
            }

            // Reading order: left to right, banded into rows so the output is stable.
            boxes.Sort(delegate (int[] a, int[] b)
            {
                int ay = (a[1] + a[3]) / 2, by = (b[1] + b[3]) / 2;
                int ax = (a[0] + a[2]) / 2, bx = (b[0] + b[2]) / 2;
                if (Math.Abs(ay - by) > 30) return ay.CompareTo(by);
                return ax.CompareTo(bx);
            });

            Console.WriteLine("# " + path + "  (" + w + " x " + h + ")");
            Console.WriteLine("# idx  cx   cy    w   h   left  top");
            for (int i = 0; i < boxes.Count; i++)
            {
                int[] bx2 = boxes[i];
                int bw = bx2[2] - bx2[0] + 1, bh = bx2[3] - bx2[1] + 1;
                int cx = bx2[0] + bw / 2, cy = bx2[1] + bh / 2;
                Console.WriteLine("{0,4}  {1,4} {2,4}  {3,3} {4,3}  {5,4} {6,4}",
                    i + 1, cx, cy, bw, bh, bx2[0], bx2[1]);
            }
            Console.WriteLine("TOTAL_BOXES: " + boxes.Count);
        }
    }
}
