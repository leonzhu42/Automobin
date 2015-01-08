using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using Emgu.CV;
using Emgu.CV.Structure;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;

namespace Automobin
{
	public static class ImageExtentions
	{
		private const int MaxDepthDistance = 4000;
		private const int MinDepthDistance = 850;
		private const int MaxDepthDistanceOffset = 3150;

		public static BitmapSource SliceDepthImage(this DepthImageFrame image, int min = 20, int max = 1000)
		{
			int width = image.Width;
			int height = image.Height;

			//var depthFrame = image.Image.Bits;
			short[] rawDepthData = new short[image.PixelDataLength];
			image.CopyPixelDataTo(rawDepthData);

			var pixels = new byte[height * width * 4];

			const int BlueIndex = 0;
			const int GreenIndex = 1;
			const int RedIndex = 2;

			for (int depthIndex = 0, colorIndex = 0;
				depthIndex < rawDepthData.Length && colorIndex < pixels.Length;
				depthIndex++, colorIndex += 4)
			{

				// Calculate the distance represented by the two depth bytes
				int depth = rawDepthData[depthIndex] >> DepthImageFrame.PlayerIndexBitmaskWidth;

				// Map the distance to an intesity that can be represented in RGB
				var intensity = CalculateIntensityFromDistance(depth);

				if (depth > min && depth < max)
				{
					// Apply the intensity to the color channels
					pixels[colorIndex + BlueIndex] = intensity; //blue
					pixels[colorIndex + GreenIndex] = intensity; //green
					pixels[colorIndex + RedIndex] = intensity; //red                    
				}
			}

			return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
		}

		public static BitmapSource SliceColorImage(this ColorImageFrame image)
		{
			int width = image.Width;
			int height = image.Height;

			byte[] colorData = new Byte[image.PixelDataLength];
			image.CopyPixelDataTo(colorData);
			return BitmapSource.Create(width, height, 96.0, 96.0, PixelFormats.Bgr32, null, colorData, width * 4);
		}

		public static byte CalculateIntensityFromDistance(int distance)
		{
			// This will map a distance value to a 0 - 255 range
			// for the purposes of applying the resulting value
			// to RGB pixels.
			int newMax = distance - MinDepthDistance;
			if (newMax > 0)
				return (byte)(255 - (255 * newMax
				/ (MaxDepthDistanceOffset)));
			else
				return (byte)255;
		}

		public static System.Drawing.Bitmap ToBitmap(this BitmapSource bitmapsource)
		{
			System.Drawing.Bitmap bitmap;
			using (var outStream = new MemoryStream())
			{
				// from System.Media.BitmapImage to System.Drawing.Bitmap
				BitmapEncoder enc = new BmpBitmapEncoder();
				enc.Frames.Add(BitmapFrame.Create(bitmapsource));
				enc.Save(outStream);
				bitmap = new System.Drawing.Bitmap(outStream);
				return bitmap;
			}
		}

		[DllImport("gdi32")]
		private static extern int DeleteObject(IntPtr o);

		public static BitmapSource ToBitmapSource(IImage image)
		{
			using (System.Drawing.Bitmap source = image.Bitmap)
			{
				IntPtr ptr = source.GetHbitmap(); //obtain the Hbitmap

				BitmapSource bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
					ptr,
					IntPtr.Zero,
					Int32Rect.Empty,
					System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

				DeleteObject(ptr); //release the HBitmap
				return bs;
			}
		}
	}
}
