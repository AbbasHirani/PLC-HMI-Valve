using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Crops a region of an illustration and scales it up, so the printed CM labels can be read
// accurately enough to associate each one with the valve box the detector found.
class CropImage
{
    static void Main(string[] args)
    {
        string src = args[0], dst = args[1];
        int x = int.Parse(args[2]), y = int.Parse(args[3]);
        int w = int.Parse(args[4]), h = int.Parse(args[5]);
        float scale = args.Length > 6 ? float.Parse(args[6]) : 2f;

        using (Bitmap bmp = new Bitmap(src))
        {
            if (x + w > bmp.Width) w = bmp.Width - x;
            if (y + h > bmp.Height) h = bmp.Height - y;
            using (Bitmap outBmp = new Bitmap((int)(w * scale), (int)(h * scale)))
            using (Graphics g = Graphics.FromImage(outBmp))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, new Rectangle(0, 0, outBmp.Width, outBmp.Height),
                            new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
                outBmp.Save(dst, ImageFormat.Png);
            }
        }
        Console.WriteLine("wrote " + dst);
    }
}
