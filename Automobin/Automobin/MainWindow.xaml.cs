using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
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
using ImageManipulationExtensionMethods;

namespace Automobin
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private KinectSensor sensor;
		//Depth and color images
		private WriteableBitmap depthColorBitmap;
		private WriteableBitmap colorColorBitmap;
		private DepthImagePixel[] depthPixels;
		private byte[] depthColorPixels;
		private byte[] colorColorPixels;
		//Skeleton image
		private const float RenderWidth = 640.0f;
		private const float RenderHeight = 480.0f;
		private const double JointThickness = 3;
		private const double BodyCenterThickness = 10;
		private const double ClipBoundsThickness = 10;
		private readonly Brush centerPointBrush = Brushes.Blue;
		private readonly Brush trackedJointBrush = new SolidColorBrush(Color.FromArgb(255, 68, 192, 68));
		private readonly Brush inferredJointBrush = Brushes.Yellow;
		private readonly Pen trackedBonePen = new Pen(Brushes.Green, 6);
		private readonly Pen inferredBonePen = new Pen(Brushes.Gray, 1);
		private DrawingGroup drawingGroup;
		private DrawingImage skeletonImage;
		//Motion tracking
		private static int MaxFeatures = 100;
		private static double NormThreshold = 5;
		bool firstRun = true;
		Image<Gray, Byte> prev = new Image<Gray, Byte>(0, 0, new Gray(0));
		Image<Gray, Byte> curr = new Image<Gray, Byte>(0, 0, new Gray(0));
		Image<Bgr, Byte> displayedImage;

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
				this.drawingGroup = new DrawingGroup();
				this.skeletonImage = new DrawingImage(this.drawingGroup);
				this.sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
				this.sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
				this.sensor.SkeletonStream.Enable();
				this.depthPixels = new DepthImagePixel[this.sensor.DepthStream.FramePixelDataLength];
				this.depthColorPixels = new byte[this.sensor.DepthStream.FramePixelDataLength * sizeof(int)];
				this.colorColorPixels = new byte[this.sensor.ColorStream.FramePixelDataLength];
				this.depthColorBitmap = new WriteableBitmap(this.sensor.DepthStream.FrameWidth, this.sensor.DepthStream.FrameHeight, 96.0, 96.0, PixelFormats.Bgr32, null);
				this.colorColorBitmap = new WriteableBitmap(this.sensor.ColorStream.FrameWidth, this.sensor.ColorStream.FrameHeight, 96.0, 96.0, PixelFormats.Bgr32, null);
				//this.DepthImage.Source = this.depthColorBitmap;
				//this.ColorImage.Source = this.colorColorBitmap;
				this.SkeletonImage.Source = this.skeletonImage;
				//this.sensor.DepthFrameReady += this.SensorDepthFrameReady;
				//this.sensor.ColorFrameReady += this.SensorColorFrameReady;
				//this.sensor.SkeletonFrameReady += this.SensorSkeletonFrameReady;
				this.sensor.AllFramesReady += this.SensorAllFramesReady;
				CvInvoke.cvNamedWindow("Motion Tracking");
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

		/*
		private void SensorColorFrameReady(object sender, ColorImageFrameReadyEventArgs e)
		{
			BitmapSource colorBitmap;
			bool firstRun = true;
			using(ColorImageFrame colorFrame = e.OpenColorImageFrame())
			{
				if(colorFrame != null)
				{
					colorFrame.CopyPixelDataTo(this.colorColorPixels);
					this.colorColorBitmap.WritePixels(
						new Int32Rect(0, 0, this.colorColorBitmap.PixelWidth, this.colorColorBitmap.PixelHeight),
						this.colorColorPixels,
						this.colorColorBitmap.PixelWidth * sizeof(int),
						0);
					
					//Convert frame into OpenCV image
					colorBitmap = colorFrame.SliceColorImage();
					currImage = new Image<Bgr, Byte>(colorBitmap.ToBitmap());
					
					//Lucas Kanade

					
				}
			}
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
						this.depthColorPixels[colorPixelIndex++] = intensity;

						// Write out green byte
						this.depthColorPixels[colorPixelIndex++] = intensity;

						// Write out red byte                        
						this.depthColorPixels[colorPixelIndex++] = intensity;

						// We're outputting BGR, the last byte in the 32 bits is unused so skip it
						// If we were outputting BGRA, we would write alpha here.
						++colorPixelIndex;
					}

					// Write the pixel data into our bitmap
					this.depthColorBitmap.WritePixels(
						new Int32Rect(0, 0, this.depthColorBitmap.PixelWidth, this.depthColorBitmap.PixelHeight),
						this.depthColorPixels,
						this.depthColorBitmap.PixelWidth * sizeof(int),
						0);
				}
			}
		}

		
		private void SensorSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
		{
			Skeleton[] skeletons = new Skeleton[0];

			using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
			{
				if(skeletonFrame != null)
				{
					skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
					skeletonFrame.CopySkeletonDataTo(skeletons);
				}
			}

			using(DrawingContext dc = this.drawingGroup.Open())
			{
				dc.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, RenderWidth, RenderHeight));

				if(skeletons.Length != 0)
				{
					foreach (Skeleton skel in skeletons)
					{
						RenderClippedEdges(skel, dc);
						if (skel.TrackingState == SkeletonTrackingState.Tracked)
							this.DrawBonesAndJoints(skel, dc);
						else if (skel.TrackingState == SkeletonTrackingState.PositionOnly)
							dc.DrawEllipse(
								this.centerPointBrush,
								null,
								this.SkeletonPointToScreen(skel.Position),
								BodyCenterThickness,
								BodyCenterThickness);
					}
				}
				this.drawingGroup.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, RenderWidth, RenderHeight));
			}
		}
		*/

		private void SensorAllFramesReady(object sender, AllFramesReadyEventArgs e)
		{
			Skeleton[] skeletons = new Skeleton[0];

			using(ColorImageFrame colorFrame = e.OpenColorImageFrame())
			{
				if(colorFrame != null)
				{
					using(DepthImageFrame depthFrame = e.OpenDepthImageFrame())
					{
						if(depthFrame != null)
						{
							//Get the skeletons
							using(SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
							{
								if(skeletonFrame != null)
								{
									skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
									skeletonFrame.CopySkeletonDataTo(skeletons);
								}
							}

							//Draw the skeletons
							using(DrawingContext dc = this.drawingGroup.Open())
							{
								dc.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, RenderWidth, RenderHeight));

								if(skeletons.Length != 0)
								{
									foreach (Skeleton skel in skeletons)
									{
										RenderClippedEdges(skel, dc);
										if (skel.TrackingState == SkeletonTrackingState.Tracked)
											this.DrawBonesAndJoints(skel, dc);
										else if (skel.TrackingState == SkeletonTrackingState.PositionOnly)
											dc.DrawEllipse(
												this.centerPointBrush,
												null,
												this.SkeletonPointToScreen(skel.Position),
												BodyCenterThickness,
												BodyCenterThickness);
									}
								}
								this.drawingGroup.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, RenderWidth, RenderHeight));
							}

							//Lucas-Kanade
							displayedImage = colorFrame.ToOpenCVImage<Bgr, Byte>();
							curr = colorFrame.ToOpenCVImage<Gray, Byte>();
							if (firstRun)
								firstRun = false;
							else
							{
								//CvInvoke.cvShowImage("prev", prev);
								//CvInvoke.cvShowImage("curr", curr);
								Image<Bgr, Byte> eigImage = new Image<Bgr, Byte>(prev.Size);
								Image<Bgr, Byte> tmpImage = new Image<Bgr, Byte>(prev.Size);
								int featureCount = MaxFeatures;
								System.Drawing.PointF[] prevFeatures = new System.Drawing.PointF[MaxFeatures];
								GCHandle hObject = GCHandle.Alloc(prevFeatures, GCHandleType.Pinned);
								IntPtr pObject = hObject.AddrOfPinnedObject();
								CvInvoke.cvGoodFeaturesToTrack(
									prev.Ptr,
									eigImage.Ptr,
									tmpImage.Ptr,
									pObject,
									ref featureCount,
									0.01,
									0.5,
									IntPtr.Zero,
									3,
									0,
									0.04);

								MCvTermCriteria criteria = new MCvTermCriteria(20, 0.03);
								criteria.type = Emgu.CV.CvEnum.TERMCRIT.CV_TERMCRIT_EPS | Emgu.CV.CvEnum.TERMCRIT.CV_TERMCRIT_ITER;

								CvInvoke.cvFindCornerSubPix(
									prev,
									prevFeatures,
									featureCount,
									new System.Drawing.Size(10, 10),
									new System.Drawing.Size(-1, -1),
									criteria);
								System.Drawing.PointF[] currFeatures = new System.Drawing.PointF[MaxFeatures];
								Byte[] status = new Byte[MaxFeatures];
								float[] trackError = new float[MaxFeatures];

								System.Drawing.Size winSize = new System.Drawing.Size(prev.Width + 8, curr.Height / 3);

								Image<Bgr, Int32> prevPyrBuffer = new Image<Bgr, Int32>(winSize);
								Image<Bgr, Int32> currPyrBuffer = new Image<Bgr, Int32>(winSize);
								CvInvoke.cvCalcOpticalFlowPyrLK(
									prev,
									curr,
									prevPyrBuffer,
									currPyrBuffer,
									prevFeatures,
									currFeatures,
									featureCount,
									new System.Drawing.Size(10, 10),
									5,
									status,
									trackError,
									criteria,
									Emgu.CV.CvEnum.LKFLOW_TYPE.DEFAULT);

								List<PlaneVector> vectors = new List<PlaneVector>();
								MCvScalar color = new MCvScalar(0, 0, 255);
								for (int i = 0; i < featureCount; i++)
								{
									if (status[i] != 1 || trackError[i] > 550)
										continue;
									PlaneVector planeVector = new PlaneVector(prevFeatures[i], currFeatures[i]);
									if (planeVector.getNorm() >= NormThreshold)
									{
										vectors.Add(planeVector);
										System.Drawing.Point prevPoint = new System.Drawing.Point((int)prevFeatures[i].X, (int)prevFeatures[i].Y);
										System.Drawing.Point currPoint = new System.Drawing.Point((int)currFeatures[i].X, (int)currFeatures[i].Y);
										CvInvoke.cvLine(displayedImage, prevPoint, currPoint, color, 2, Emgu.CV.CvEnum.LINE_TYPE.CV_AA, 0);
									}
								}
								CvInvoke.cvShowImage("Motion Tracking", displayedImage);
							}
							prev = curr.Copy();
						}
					}
				}
			}
		}
		 
		private static void RenderClippedEdges(Skeleton skeleton, DrawingContext drawingContext)
		{
			if (skeleton.ClippedEdges.HasFlag(FrameEdges.Bottom))
			{
				drawingContext.DrawRectangle(
					Brushes.Red,
					null,
					new Rect(0, RenderHeight - ClipBoundsThickness, RenderWidth, ClipBoundsThickness));
			}

			if (skeleton.ClippedEdges.HasFlag(FrameEdges.Top))
			{
				drawingContext.DrawRectangle(
					Brushes.Red,
					null,
					new Rect(0, 0, RenderWidth, ClipBoundsThickness));
			}

			if (skeleton.ClippedEdges.HasFlag(FrameEdges.Left))
			{
				drawingContext.DrawRectangle(
					Brushes.Red,
					null,
					new Rect(0, 0, ClipBoundsThickness, RenderHeight));
			}

			if (skeleton.ClippedEdges.HasFlag(FrameEdges.Right))
			{
				drawingContext.DrawRectangle(
					Brushes.Red,
					null,
					new Rect(RenderWidth - ClipBoundsThickness, 0, ClipBoundsThickness, RenderHeight));
			}
		}

		private void DrawBonesAndJoints(Skeleton skeleton, DrawingContext drawingContext)
		{
			// Render Torso
            this.DrawBone(skeleton, drawingContext, JointType.Head, JointType.ShoulderCenter);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderLeft);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderRight);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.Spine);
            this.DrawBone(skeleton, drawingContext, JointType.Spine, JointType.HipCenter);
            this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipLeft);
            this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipRight);

            // Left Arm
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderLeft, JointType.ElbowLeft);
            this.DrawBone(skeleton, drawingContext, JointType.ElbowLeft, JointType.WristLeft);
            this.DrawBone(skeleton, drawingContext, JointType.WristLeft, JointType.HandLeft);

            // Right Arm
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderRight, JointType.ElbowRight);
            this.DrawBone(skeleton, drawingContext, JointType.ElbowRight, JointType.WristRight);
            this.DrawBone(skeleton, drawingContext, JointType.WristRight, JointType.HandRight);

            // Left Leg
            this.DrawBone(skeleton, drawingContext, JointType.HipLeft, JointType.KneeLeft);
            this.DrawBone(skeleton, drawingContext, JointType.KneeLeft, JointType.AnkleLeft);
            this.DrawBone(skeleton, drawingContext, JointType.AnkleLeft, JointType.FootLeft);

            // Right Leg
            this.DrawBone(skeleton, drawingContext, JointType.HipRight, JointType.KneeRight);
            this.DrawBone(skeleton, drawingContext, JointType.KneeRight, JointType.AnkleRight);
            this.DrawBone(skeleton, drawingContext, JointType.AnkleRight, JointType.FootRight);
 
            // Render Joints
            foreach (Joint joint in skeleton.Joints)
            {
                Brush drawBrush = null;

                if (joint.TrackingState == JointTrackingState.Tracked)
                {
                    drawBrush = this.trackedJointBrush;                    
                }
                else if (joint.TrackingState == JointTrackingState.Inferred)
                {
                    drawBrush = this.inferredJointBrush;                    
                }

                if (drawBrush != null)
                {
                    drawingContext.DrawEllipse(drawBrush, null, this.SkeletonPointToScreen(joint.Position), JointThickness, JointThickness);
                }
            }
		}

		private Point SkeletonPointToScreen(SkeletonPoint skelPoint)
		{
			DepthImagePoint depthPoint = this.sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(skelPoint, DepthImageFormat.Resolution640x480Fps30);
			return new Point(depthPoint.X, depthPoint.Y);
		}

		private void DrawBone(Skeleton skeleton, DrawingContext drawingContext, JointType jointType0, JointType jointType1)
		{
			Joint joint0 = skeleton.Joints[jointType0];
			Joint joint1 = skeleton.Joints[jointType1];

			// If we can't find either of these joints, exit
			if (joint0.TrackingState == JointTrackingState.NotTracked ||
				joint1.TrackingState == JointTrackingState.NotTracked)
			{
				return;
			}

			// Don't draw if both points are inferred
			if (joint0.TrackingState == JointTrackingState.Inferred &&
				joint1.TrackingState == JointTrackingState.Inferred)
			{
				return;
			}

			// We assume all drawn bones are inferred unless BOTH joints are tracked
			Pen drawPen = this.inferredBonePen;
			if (joint0.TrackingState == JointTrackingState.Tracked && joint1.TrackingState == JointTrackingState.Tracked)
			{
				drawPen = this.trackedBonePen;
			}

			drawingContext.DrawLine(drawPen, this.SkeletonPointToScreen(joint0.Position), this.SkeletonPointToScreen(joint1.Position));
		}

		private void ButtonScreenshotClick(object sender, RoutedEventArgs e)
		{
			//Save depth image
			{
				if (this.sensor == null)
				{
					this.statusBarText.Text = Properties.Resources.ConnectDeviceFirst;
					return;
				}
				BitmapEncoder encoder = new PngBitmapEncoder();
				encoder.Frames.Add(BitmapFrame.Create(this.depthColorBitmap));
				string time = System.DateTime.Now.ToString("hh'-'mm'-'ss", CultureInfo.CurrentCulture.DateTimeFormat);
				string myPhotos = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
				string path = System.IO.Path.Combine(myPhotos, "Depth " + time + ".png");

				try
				{
					using (FileStream fs = new FileStream(path, FileMode.Create))
					{
						encoder.Save(fs);
					}
					this.statusBarText.Text = string.Format("{0} {1}", Properties.Resources.ScreenshotWriteSuccess, path);
				}
				catch (IOException)
				{
					this.statusBarText.Text = string.Format("{0} {1}", Properties.Resources.ScreenshotWriteFailed, path);
				}
			}
			//Save color image
			{
				BitmapEncoder encoder = new PngBitmapEncoder();
				encoder.Frames.Add(BitmapFrame.Create(this.colorColorBitmap));
				string time = System.DateTime.Now.ToString("hh'-'mm'-'ss", CultureInfo.CurrentCulture.DateTimeFormat);
				string myPhotos = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
				string path = System.IO.Path.Combine(myPhotos, "Color " + time + ".png");

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
}
