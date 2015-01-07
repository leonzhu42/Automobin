using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Kinect;
using Emgu.CV;
using Emgu.Util;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;

namespace Automobin
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private KinectSensor sensor;
		private WriteableBitmap colorBitmap;
		private DepthImagePixel[] depthPixels;
		private byte[] colorPixels;

		public MainWindow()
		{
			InitializeComponent();
		}

		private void WindowLoaded(object sender, RoutedEventArgs e)
		{
			foreach(var potentialSensor in KinectSensor.KinectSensors)
				if(potentialSensor.Status == KinectStatus.Connected)
				{
					this.sensor = potentialSensor;
					break;
				}
			if(this.sensor != null)
			{
				this.sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
				this.depthPixels = new DepthImagePixel[this.sensor.DepthStream.FramePixelDataLength];
				this.colorPixels = new byte[this.sensor.DepthStream.FramePixelDataLength * sizeof(int)];
				this.colorBitmap = new WriteableBitmap(this.sensor.DepthStream.FrameWidth, this.sensor.DepthStream.FrameHeight, 96.0, 96.0, PixelFormats.Bgr32, null);
				this.Image.Source = this.colorBitmap;
				this.sensor.DepthFrameReady += this.SensorDepthFrameReady;
				try
				{
					this.sensor.Start();
				}
				catch(IOException)
				{
					this.sensor = null;
				}
			}
			if (this.sensor == null)
				this.statusBarText.Text = Properties.Resources.NoKinectReady;
		}

		private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (this.sensor != null)
				this.sensor.Stop();
		}

		private void SensorDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
		{
			using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
			{
				if (depthFrame != null)
				{
					// Copy the pixel data from the image to a temporary array
					depthFrame.CopyDepthImagePixelDataTo(this.depthPixels);

					// Get the min and max reliable depth for the current frame
					int minDepth = depthFrame.MinDepth;
					int maxDepth = depthFrame.MaxDepth;

					// Convert the depth to RGB
					int colorPixelIndex = 0;
					for (int i = 0; i < this.depthPixels.Length; ++i)
					{
						// Get the depth for this pixel
						short depth = depthPixels[i].Depth;

						// To convert to a byte, we're discarding the most-significant
						// rather than least-significant bits.
						// We're preserving detail, although the intensity will "wrap."
						// Values outside the reliable depth range are mapped to 0 (black).

						// Note: Using conditionals in this loop could degrade performance.
						// Consider using a lookup table instead when writing production code.
						// See the KinectDepthViewer class used by the KinectExplorer sample
						// for a lookup table example.
						byte intensity = (byte)(depth >= minDepth && depth <= maxDepth ? depth : 0);

						// Write out blue byte
						this.colorPixels[colorPixelIndex++] = intensity;

						// Write out green byte
						this.colorPixels[colorPixelIndex++] = intensity;

						// Write out red byte                        
						this.colorPixels[colorPixelIndex++] = intensity;

						// We're outputting BGR, the last byte in the 32 bits is unused so skip it
						// If we were outputting BGRA, we would write alpha here.
						++colorPixelIndex;
					}

					// Write the pixel data into our bitmap
					this.colorBitmap.WritePixels(
						new Int32Rect(0, 0, this.colorBitmap.PixelWidth, this.colorBitmap.PixelHeight),
						this.colorPixels,
						this.colorBitmap.PixelWidth * sizeof(int),
						0);
				}
			}
		}

		private void ButtonScreenshotClick(object sender, RoutedEventArgs e)
		{
			if(this.sensor == null)
			{
				this.statusBarText.Text = Properties.Resources.ConnectDeviceFirst;
				return;
			}
			BitmapEncoder encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(this.colorBitmap));
			string time = System.DateTime.Now.ToString("hh'-'mm'-'ss", CultureInfo.CurrentCulture.DateTimeFormat);
			string myPhotos = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
			string path = System.IO.Path.Combine(myPhotos, "KinectSnapshot-" + time + ".png");

			try
			{
				using(FileStream fs = new FileStream(path, FileMode.Create))
				{
					encoder.Save(fs);
				}
				this.statusBarText.Text = string.Format("{0} {1}", Properties.Resources.ScreenshotWriteSuccess, path);
			}
			catch(IOException)
			{
				this.statusBarText.Text = string.Format("{0} {1}", Properties.Resources.ScreenshotWriteFailed, path);
			}
		}
	}

}
