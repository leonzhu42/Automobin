using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
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
using Microsoft.Speech.AudioFormat;
using Microsoft.Speech.Recognition;
using Microsoft.Kinect;
using Coding4Fun.Kinect;
using Coding4Fun.Kinect.Wpf;
using Coding4Fun.Kinect.Wpf.Controls;
using Emgu.CV;
using Emgu.Util;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using Newtonsoft.Json;
using TCPServer;
using ImageManipulationExtensionMethods;


namespace Automobin
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		// Kinect sensor
		private KinectSensor sensor;
		// Speech recognition engine
		private SpeechRecognitionEngine speechEngine;
		// Span elements to select recognized text
		private List<Span> recognitionSpans;
		// Server
		private Server server;
		// Current state
		// 0: Standby
		// 1: Running
		private int state = 0;
		// Depth images
		private DepthImagePixel[] depthPixels;
		private WriteableBitmap depthColorBitmap;
		private byte[] depthColorPixels;
		private int frameWidth;
		private int frameHeight;
		private int bytesPerPixel;
		private Image<Gray, Int32> frameImage;
		//Local image
		private int localWidth = 50;
		private int localHeight = 50;
		// Skeleton image
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
		// Hand Positions
		private DepthImagePoint rightHandDepthPoint;
		private SkeletonPoint rightHandSkeletonPoint;
		private DepthImagePoint leftHandDepthPoint;
		private SkeletonPoint leftHandSkeletonPoint;
		// Trash Position
		private DepthImagePoint trashDepthPoint;
		private List<DepthImagePoint> trashDepthPoints;
		// Time between frames
		private bool firstFrame = true;
		private List<long> frameTimes;
		private Stopwatch currentStopwatch;
		// Velocity vectors
		private List<Velocity> velocities;
		private List<DepthImagePoint> landingPoints;
		// Thresholds
		private static double LandingThreshold = 5.0;
		private static double ObjectThreshold = 10.0;
		private static double BackgroundColor = 255;
		// Gravity constant
		private static double g = 9.794;
		// Notify icon
		private System.Windows.Forms.NotifyIcon notifyIcon;

		public MainWindow()
		{
			InitializeComponent();
			InitialTray();
		}

		private void InitialTray()
		{
			notifyIcon = new System.Windows.Forms.NotifyIcon();
			notifyIcon.Text = "Automobin";
			notifyIcon.Icon = new System.Drawing.Icon("Icon.ico");
			notifyIcon.Visible = true;
			notifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(notifyIcon_MouseClick);

			System.Windows.Forms.MenuItem menuShow = new System.Windows.Forms.MenuItem("Show");
			System.Windows.Forms.MenuItem menu = new System.Windows.Forms.MenuItem("Menu", new System.Windows.Forms.MenuItem[] { menuShow });
			menu.Click += new EventHandler(menu_Click);

			System.Windows.Forms.MenuItem menuExit = new System.Windows.Forms.MenuItem("Exit");
			menuExit.Click += new EventHandler(exit_Click);

			System.Windows.Forms.MenuItem[] children = new System.Windows.Forms.MenuItem[] { menu, menuExit };
			notifyIcon.ContextMenu = new System.Windows.Forms.ContextMenu(children);

			this.StateChanged += new EventHandler(SysTray_StateChanged);
		}

		private void SysTray_StateChanged(object sender, EventArgs e)
		{
			if (this.WindowState == WindowState.Minimized)
				this.Visibility = Visibility.Hidden;
		}
		
		private void menu_Click(object sender, EventArgs e)
		{
			this.Visibility = Visibility.Visible;
		}

		private void exit_Click(object sender, EventArgs e)
		{
			if(System.Windows.MessageBox.Show("Are you sure to exit?", "Automobin", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
			{
				notifyIcon.Dispose();

				if (this.sensor != null)
				{
					this.sensor.AudioSource.Stop();
					this.sensor.Stop();
					this.sensor = null;
				}

				if (this.speechEngine != null)
				{
					this.speechEngine.SpeechRecognized -= SpeechRecognized;
					this.speechEngine.SpeechRecognitionRejected -= SpeechRejected;
					this.speechEngine.RecognizeAsyncStop();
				}

				System.Windows.Application.Current.Shutdown();
			}
		}

		private void notifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			if(e.Button == System.Windows.Forms.MouseButtons.Left)
			{
				if (this.Visibility == Visibility.Visible)
					this.Visibility = Visibility.Hidden;
				else
				{
					this.Visibility = Visibility.Visible;
					this.Activate();
				}
			}
		}

		private static RecognizerInfo GetKinectRecognizer()
		{
			foreach(RecognizerInfo recognizer in SpeechRecognitionEngine.InstalledRecognizers())
			{
				string value;
				recognizer.AdditionalInfo.TryGetValue("Kinect", out value);
				if ("True".Equals(value, StringComparison.OrdinalIgnoreCase) && "en-US".Equals(recognizer.Culture.Name, StringComparison.OrdinalIgnoreCase))
					return recognizer;
			}
			return null;
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
				/*
				this.drawingGroup = new DrawingGroup();
				this.skeletonImage = new DrawingImage(this.drawingGroup);
				this.sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
				//this.sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
				this.sensor.SkeletonStream.Enable();
				this.depthPixels = new DepthImagePixel[this.sensor.DepthStream.FramePixelDataLength];
				//this.depthColorPixels = new byte[this.sensor.DepthStream.FramePixelDataLength * sizeof(int)];
				//this.DepthImage.Source = this.depthColorBitmap;
				//this.ColorImage.Source = this.colorColorBitmap;
				this.SkeletonImage.Source = this.skeletonImage;
				//this.sensor.DepthFrameReady += this.SensorDepthFrameReady;
				//this.sensor.ColorFrameReady += this.SensorColorFrameReady;
				//this.sensor.SkeletonFrameReady += this.SensorSkeletonFrameReady;
				this.sensor.AllFramesReady += this.SensorAllFramesReady;
				*/
				try
				{
					this.sensor.Start();
					server = new Server();
				}
				catch(IOException)
				{
					this.sensor = null;
				}
			}
			if (this.sensor == null)
			{
				this.statusBarText.Text = Properties.Resources.NoKinectReady;
				return;
			}


			RecognizerInfo recognizerInfo = GetKinectRecognizer();
			if(recognizerInfo != null)
			{
				recognitionSpans = new List<Span> { startSpan };
				this.speechEngine = new SpeechRecognitionEngine(recognizerInfo.Id);
				
				Choices command = new Choices();
				command.Add(new SemanticResultValue("Okay trash", "START"));

				GrammarBuilder grammarBuilder = new GrammarBuilder { Culture = recognizerInfo.Culture };
				grammarBuilder.Append(command);

				Grammar grammar = new Grammar(grammarBuilder);

				speechEngine.LoadGrammar(grammar);

				speechEngine.SpeechRecognized += SpeechRecognized;
				speechEngine.SpeechRecognitionRejected += SpeechRejected;

				// For long recognition sessions, add the following code.
				//speechEngine.UpdateRecognizerSetting("AdaptationOn", 0);

				
				speechEngine.SetInputToAudioStream(
					sensor.AudioSource.Start(),
					new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
				
				//speechEngine.SetInputToDefaultAudioDevice();
				speechEngine.RecognizeAsync(RecognizeMode.Multiple);
			}
		}

		private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e) { }

		private void SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
		{
			const double ConfidenceThreshold = 0.3;

			if(e.Result.Confidence >= ConfidenceThreshold)
			{
				if(e.Result.Semantics.Value.ToString() == "START")
				{
					// Start depth and skeleton
					this.drawingGroup = new DrawingGroup();
					this.skeletonImage = new DrawingImage(this.drawingGroup);
					this.sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
					//this.sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
					this.sensor.SkeletonStream.Enable();
					this.depthPixels = new DepthImagePixel[this.sensor.DepthStream.FramePixelDataLength];
					//this.depthColorPixels = new byte[this.sensor.DepthStream.FramePixelDataLength * sizeof(int)];
					//this.DepthImage.Source = this.depthColorBitmap;
					//this.ColorImage.Source = this.colorColorBitmap;
					this.SkeletonImage.Source = this.skeletonImage;
					//this.sensor.DepthFrameReady += this.SensorDepthFrameReady;
					//this.sensor.ColorFrameReady += this.SensorColorFrameReady;
					//this.sensor.SkeletonFrameReady += this.SensorSkeletonFrameReady;
					this.sensor.AllFramesReady += this.SensorAllFramesReady;
				}
			}
		}

		private void SpeechRejected(object sender, SpeechRecognitionRejectedEventArgs e) { }

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
			// Get the skeleton frame
			Skeleton[] skeletons = new Skeleton[0];

			// Get the skeletons
			using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
			{
				if (skeletonFrame != null)
				{
					skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
					skeletonFrame.CopySkeletonDataTo(skeletons);
				}
				else
					return;
			}

			// Draw the skeletons
			using (DrawingContext dc = this.drawingGroup.Open())
			{
				dc.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, RenderWidth, RenderHeight));
				if (skeletons.Length != 0)
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

			// Get the depth
			using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
			{
				if (depthFrame != null)
				{
					frameWidth = depthFrame.Width;
					frameHeight = depthFrame.Height;
					bytesPerPixel = depthFrame.BytesPerPixel;
					// Convert the image to a Emgu image
					frameImage = depthFrame.ToOpenCVImage<Gray, Int32>();
					// Copy the pixel data from the image to a temporary array
					depthFrame.CopyDepthImagePixelDataTo(this.depthPixels);
				}
				else
					return;
			}

			// Check the state
			if (state == 0)
			{
				// Standby. Check whether any object nearby is at approximately the same depth as user's hand.
				// Choose the skeleton to track
				Skeleton skeleton = (from s in skeletons
									 where s.TrackingState == SkeletonTrackingState.Tracked
									 select s).FirstOrDefault();
				// Right hand
				Joint rightHand = skeleton.Joints[JointType.HandRight];
				rightHandSkeletonPoint = rightHand.Position;
				rightHandDepthPoint = sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(rightHandSkeletonPoint, sensor.DepthStream.Format);
				// Left hand
				Joint leftHand = skeleton.Joints[JointType.HandLeft];
				leftHandSkeletonPoint = leftHand.Position;
				leftHandDepthPoint = sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(leftHandSkeletonPoint, sensor.DepthStream.Format);

				DepthImagePoint[] handDepthPoints = { rightHandDepthPoint, leftHandDepthPoint };

				// Look for object nearby
				bool trashFound = false;
				FindNearbyObject(handDepthPoints, ref trashDepthPoint, ref trashFound);
				if (trashFound)
				{
					currentStopwatch.Start();
					trashDepthPoints = new List<DepthImagePoint>();
					trashDepthPoints.Add(trashDepthPoint);
					velocities = new List<Velocity>();
					landingPoints = new List<DepthImagePoint>();
					state = 1;
				}
			}
			else
			{
				// Running. Keep tracking the object, communicate with the bin, until caught by the bin.
				currentStopwatch.Stop();
				long time = currentStopwatch.ElapsedMilliseconds;
				currentStopwatch.Restart();
				UpdateTrashLocation(ref trashDepthPoint);
				DepthImagePoint lastTrashDepthPoint = trashDepthPoints[trashDepthPoints.Count - 1];
				Velocity velocity = new Velocity(lastTrashDepthPoint.X, lastTrashDepthPoint.Y, lastTrashDepthPoint.Depth, trashDepthPoint.X, trashDepthPoint.Y, trashDepthPoint.Depth, time);
				trashDepthPoints.Add(trashDepthPoint);
				frameTimes.Add(time);

				DepthImagePoint landingPoint = PredictLandingPoint();
				SendLocationToBin(landingPoint);
				if (System.Math.Abs(landingPoint.Y) <= LandingThreshold)
				{
					currentStopwatch.Stop();
					trashDepthPoints.Clear();
					velocities.Clear();
					landingPoints.Clear();

					this.sensor.DepthStream.Disable();
					this.sensor.SkeletonStream.Disable();
					this.SkeletonImage.Source = null;
					this.sensor.AllFramesReady -= this.SensorAllFramesReady;
					state = 0;
				}
			}
		}

		private void UpdateTrashLocation(ref DepthImagePoint trashPoint)
		{
			int stride = frameWidth * bytesPerPixel;
			DepthImagePixel trashPixel = depthPixels[trashPoint.X * bytesPerPixel + trashPoint.Y * stride];
			byte[,] localData = new byte[localWidth, localHeight];
			
			int left = trashPoint.X - localWidth / 2;
			int down = trashPoint.Y - localHeight / 2;

			Image<Gray, Byte> localImage = new Image<Gray, Byte>(localWidth, localHeight);
			CvInvoke.cvGetSubRect(frameImage, localImage, new System.Drawing.Rectangle(left, down, localWidth, localHeight));

			Image<Gray, Byte> processedLocalImage = new Image<Gray, Byte>(localWidth, localHeight);

			CvInvoke.cvThreshold(localImage, processedLocalImage, ObjectThreshold, BackgroundColor, Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY);

			int midX = 0;
			int midY = 0;
			System.Drawing.Bitmap processedBitmap = processedLocalImage.ToBitmap();
			System.Drawing.Color white = System.Drawing.Color.White;

			// Count the total white pixel number.
			int whitePixel = 0;
			for(int i = 0; i < processedBitmap.Width; ++i)
				for(int j = 0; j < processedBitmap.Height; ++j)
					if(processedBitmap.GetPixel(i, j).Equals(white))
						whitePixel++;

			int tempWhitePixel;

			// Find midX
			tempWhitePixel = 0;
			for (midX = 0; midX < processedBitmap.Width; ++midX)
			{
				for (int j = 0; j < processedBitmap.Height; ++j)
					if (processedBitmap.GetPixel(midX, j).Equals(white))
						tempWhitePixel++;
				if (tempWhitePixel > whitePixel / 2)
					break;
			}
			
			// Find midY
			tempWhitePixel = 0;
			for (int i = 0; i < processedBitmap.Width; ++i)
			{
				for (midY = 0; midY < processedBitmap.Height; ++midY)
					if (processedBitmap.GetPixel(i, midY).Equals(white))
						tempWhitePixel++;
				if (tempWhitePixel > whitePixel)
					break;
			}

			trashPoint.X = midX;
			trashPoint.Y = midY;
			trashPoint.Depth = depthPixels[midX * bytesPerPixel + midY * stride].Depth;
		}

		private void FindNearbyObject(DepthImagePoint[] handPoints, ref DepthImagePoint trashPoint, ref bool trashFound)
		{
			foreach(DepthImagePoint handPoint in handPoints)
			{
				
			}
		}

		private DepthImagePoint PredictLandingPoint()
		{
			DepthImagePoint landingPoint = new DepthImagePoint();
			DepthImagePoint lastTrashDepthPoint = trashDepthPoints[trashDepthPoints.Count - 1];
			Velocity lastVelocity = velocities[velocities.Count - 1];

			double landingTime = ((System.Math.Sqrt(lastVelocity.getVelocityZ() * lastVelocity.getVelocityZ()) - 2 * g * lastVelocity.tail.z) - lastVelocity.getVelocityZ()) / g;
			landingPoint.X = lastTrashDepthPoint.X + (int)(lastVelocity.getVelocityX() * landingTime);
			landingPoint.Y = lastTrashDepthPoint.Y + (int)(lastVelocity.getVelocityY() * landingTime);
			landingPoint.Depth = 0;
			landingPoints.Add(landingPoint);

			DepthImagePoint prediction = new DepthImagePoint();
			long sumX = 0;
			long sumY = 0;
			long sumDepth = 0;
			foreach(DepthImagePoint point in landingPoints)
			{
				sumX += point.X;
				sumY += point.Y;
				sumDepth += point.Depth;
			}
			prediction.X = (int)sumX / landingPoints.Count;
			prediction.Y = (int)sumY / landingPoints.Count;
			prediction.Depth = (int)sumDepth / landingPoints.Count;
			return prediction;
		}

		private void SendLocationToBin(DepthImagePoint landingPoint)
		{
			StringWriter stringWriter = new StringWriter();
			JsonWriter jsonWriter = new JsonTextWriter(stringWriter);

			jsonWriter.WriteStartObject();
			jsonWriter.WritePropertyName("x");
			jsonWriter.WriteValue(landingPoint.X);
			jsonWriter.WritePropertyName("y");
			jsonWriter.WriteValue(landingPoint.Y);
			jsonWriter.WriteEndObject();
			jsonWriter.Flush();

			string message = stringWriter.GetStringBuilder().ToString();

			server.setMessage(message);
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
		}
	}
}
