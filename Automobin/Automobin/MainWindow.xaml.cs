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
		
		// JSON to send
		private string message;
		
		// Current state
		// 0: Standby
		// 1: Running
		// -1: Stopped
		private int state = -1;

		// Event handler
		private EventHandler<AllFramesReadyEventArgs> handler;
		
		// Color images
		private Image<Bgr, Int32> colorFrameImage;
		
		// Depth images
		private DepthImagePixel[] depthPixels;
		//private WriteableBitmap depthColorBitmap;
		//private byte[] depthColorPixels;
		private int depthFrameWidth;
		private int depthFrameHeight;
		private int bytesPerPixel;
		private Image<Gray, Int32> depthFrameImage;
		
		//Local image
		private int localWidth = 50;
		private int localHeight = 50;
		
		// Hand Positions
		private DepthImagePoint rightHandDepthPoint;
		private SkeletonPoint rightHandSkeletonPoint;
		private DepthImagePoint leftHandDepthPoint;
		private SkeletonPoint leftHandSkeletonPoint;
		
		// Trash Position
		private DepthImagePoint trashDepthPoint;
		private List<DepthImagePoint> trashDepthPoints;
		
		// Time between frames
		private List<long> frameTimes;
		private Stopwatch currentStopwatch;
		
		// Velocity vectors
		private List<Velocity> velocities;
		private List<DepthImagePoint> landingPoints;
		
		// Thresholds
		private static double LandingThreshold = 5.0;
		private static double ObjectThreshold = 10.0;
		private static double BackgroundColor = 255;
		private static double BlackThreshold = 30;
		
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

			System.Windows.Forms.MenuItem menuAbout = new System.Windows.Forms.MenuItem("About");
			menuAbout.Click += new EventHandler(about_Click);

			System.Windows.Forms.MenuItem menuExit = new System.Windows.Forms.MenuItem("Exit");
			menuExit.Click += new EventHandler(exit_Click);

			System.Windows.Forms.MenuItem[] children = new System.Windows.Forms.MenuItem[] { menuAbout, menuExit };
			notifyIcon.ContextMenu = new System.Windows.Forms.ContextMenu(children);

			this.StateChanged += new EventHandler(SysTray_StateChanged);
		}

		private void SysTray_StateChanged(object sender, EventArgs e)
		{
			if (this.WindowState == WindowState.Minimized)
				this.Visibility = Visibility.Hidden;
		}

		private void about_Click(object sender, EventArgs e)
		{
			MessageBox.Show("Automobin - A project by LGW and ZZY. Helps you throw trash easier.", "Automobin", MessageBoxButton.OK, MessageBoxImage.Information);
		}

		private void exit_Click(object sender, EventArgs e)
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
				this.speechEngine.RecognizeAsyncStop();
			}

			if (server != null)
			{
				server.RequestStop();
				server = null;
			}

			notifyIcon.Dispose();
			System.Windows.Application.Current.Shutdown();
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
				try
				{
					this.sensor.Start();
					server = new Server();
					handler = new EventHandler<AllFramesReadyEventArgs>(SensorAllFramesReady);
				}
				catch(IOException)
				{
					this.sensor = null;
					server = null;
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
				command.Add(new SemanticResultValue("Oh, trash!", "START"));
				command.Add(new SemanticResultValue("I've got trash!", "START"));
				command.Add(new SemanticResultValue("Hey, trash!", "START"));
				command.Add(new SemanticResultValue("Got trash!", "START"));
				command.Add(new SemanticResultValue("Throwing trash!", "START"));
				command.Add(new SemanticResultValue("Lee Guangwei", "START"));
				command.Add(new SemanticResultValue("Liu Zhizheng", "START"));

				GrammarBuilder grammarBuilder = new GrammarBuilder { Culture = recognizerInfo.Culture };
				grammarBuilder.Append(command);

				Grammar grammar = new Grammar(grammarBuilder);

				speechEngine.LoadGrammar(grammar);

				speechEngine.SpeechRecognized += SpeechRecognized;

				// For long recognition sessions, add the following code.
				//speechEngine.UpdateRecognizerSetting("AdaptationOn", 0);

				
				speechEngine.SetInputToAudioStream(
					sensor.AudioSource.Start(),
					new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
				
				//speechEngine.SetInputToDefaultAudioDevice();
				speechEngine.RecognizeAsync(RecognizeMode.Multiple);
			}
		}

		private static RecognizerInfo GetKinectRecognizer()
		{
			foreach (RecognizerInfo recognizer in SpeechRecognitionEngine.InstalledRecognizers())
			{
				string value;
				recognizer.AdditionalInfo.TryGetValue("Kinect", out value);
				if ("True".Equals(value, StringComparison.OrdinalIgnoreCase) && "en-US".Equals(recognizer.Culture.Name, StringComparison.OrdinalIgnoreCase))
					return recognizer;
			}
			return null;
		}
		
		private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e) { }

		private void DragWindow(object sender, MouseButtonEventArgs e)
		{
			this.DragMove();
		}

		private void StartTracking(object sender, MouseButtonEventArgs e)
		{
			if(!this.sensor.SkeletonStream.IsEnabled)
				this.sensor.SkeletonStream.Enable();
			this.sensor.AllFramesReady += handler;
		}

		private void SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
		{
			const double ConfidenceThreshold = 0.3;

			if(e.Result.Confidence >= ConfidenceThreshold)
			{
				if(e.Result.Semantics.Value.ToString() == "START")
				{
					state = 0;
					
					// Start color, depth and skeleton
					//this.sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
					//this.sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
					this.sensor.SkeletonStream.Enable();
					this.depthPixels = new DepthImagePixel[this.sensor.DepthStream.FramePixelDataLength];

					this.sensor.AllFramesReady += handler;
				}
			}
		}

		private void SensorAllFramesReady(object sender, AllFramesReadyEventArgs e)
		{
			if (state == -1)
				return;
			
			// Get the color
			bool colorRetrieved = true;
			using(ColorImageFrame colorFrame = e.OpenColorImageFrame())
			{
				if (colorFrame != null)
					colorFrameImage = colorFrame.ToOpenCVImage<Bgr, Int32>();
				else
					colorRetrieved = false;
			}

			// Get the skeletons
			Skeleton[] skeletons = new Skeleton[0];

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

			// Get the depth
			using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
			{
				if (depthFrame != null)
				{
					depthFrameWidth = depthFrame.Width;
					depthFrameHeight = depthFrame.Height;
					bytesPerPixel = depthFrame.BytesPerPixel;
					// Convert the image to a Emgu image
					depthFrameImage = depthFrame.ToOpenCVImage<Gray, Int32>();
					// Copy the pixel data from the image to a temporary array
					depthFrame.CopyDepthImagePixelDataTo(this.depthPixels);
				}
				else
					return;
			}

			DepthImagePoint binPoint = new DepthImagePoint();
			bool binFound = false;
			if (colorRetrieved)
			{
				// Locate Automobin
				FindAutomobin(ref binPoint, ref binFound);
			}

			// Check the state
			if (state == 0)
			{
				// Standby. Check whether any object nearby is at approximately the same depth as user's hand.
				// Choose the skeleton to track
				Skeleton skeleton = (from s in skeletons
									 where s.TrackingState == SkeletonTrackingState.Tracked
									 select s).FirstOrDefault();
				if (skeleton == null)
					return;
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
					currentStopwatch = new Stopwatch();
					currentStopwatch.Start();
					trashDepthPoints = new List<DepthImagePoint>();
					trashDepthPoints.Add(trashDepthPoint);
					velocities = new List<Velocity>();
					landingPoints = new List<DepthImagePoint>();
					frameTimes = new List<long>();
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
				Velocity velocity = new Velocity(
					lastTrashDepthPoint.X,
					lastTrashDepthPoint.Y,
					lastTrashDepthPoint.Depth,
					trashDepthPoint.X,
					trashDepthPoint.Y,
					trashDepthPoint.Depth,
					time);
				trashDepthPoints.Add(trashDepthPoint);
				frameTimes.Add(time);

				DepthImagePoint landingPoint = PredictLandingPoint();

				if (!binFound)
					SendLocationToBin(landingPoint);
				else
					SendLocationToBin(landingPoint, binPoint);

				if (System.Math.Abs(landingPoint.Y) <= LandingThreshold)
				{
					currentStopwatch.Stop();
					trashDepthPoints.Clear();
					velocities.Clear();
					landingPoints.Clear();

					this.sensor.DepthStream.Disable();
					this.sensor.SkeletonStream.Disable();
					this.sensor.AllFramesReady -= handler;
					state = -1;
				}
			}
		}
		
		private void UpdateTrashLocation(ref DepthImagePoint trashPoint)
		{
			// Get the binarilized local image.
			int stride = depthFrameWidth * bytesPerPixel;
			
			int left = trashPoint.X - localWidth / 2;
			int down = trashPoint.Y - localHeight / 2;

			Image<Gray, Byte> localImage = new Image<Gray, Byte>(localWidth, localHeight);
			CvInvoke.cvGetSubRect(depthFrameImage, localImage, new System.Drawing.Rectangle(left, down, localWidth, localHeight));

			Image<Gray, Byte> processedLocalImage = new Image<Gray, Byte>(localWidth, localHeight);

			CvInvoke.cvThreshold(localImage, processedLocalImage, ObjectThreshold, BackgroundColor, Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY);

			int midX = 0;
			int midY = 0;
			Gray white = new Gray(0);

			// Count the total white pixel number.
			int whitePixel = 0;
			for (int i = 0; i < processedLocalImage.Width; ++i)
				for (int j = 0; j < processedLocalImage.Height; ++j)
					if (Gray.Equals(processedLocalImage[i, j], white))
						whitePixel++;

			int tempWhitePixel;

			// Find midX
			tempWhitePixel = 0;
			for (midX = 0; midX < processedLocalImage.Width; ++midX)
			{
				for (int j = 0; j < processedLocalImage.Height; ++j)
					if (Gray.Equals(processedLocalImage[midX, j], white))
						tempWhitePixel++;
				if (tempWhitePixel > whitePixel / 2)
					break;
			}
			
			// Find midY
			tempWhitePixel = 0;
			for (int i = 0; i < processedLocalImage.Width; ++i)
			{
				for (midY = 0; midY < processedLocalImage.Height; ++midY)
					if (Gray.Equals(processedLocalImage[i, midY], white))
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
			foreach (DepthImagePoint handPoint in handPoints)
			{
				// Get the binarilized local image.
				int stride = depthFrameWidth * bytesPerPixel;

				int left = trashPoint.X - localWidth / 2;
				int down = trashPoint.Y - localHeight / 2;

				Image<Gray, Byte> localImage = new Image<Gray, Byte>(localWidth, localHeight);
				CvInvoke.cvGetSubRect(depthFrameImage, localImage, new System.Drawing.Rectangle(left, down, localWidth, localHeight));

				Image<Gray, Byte> processedLocalImage = new Image<Gray, Byte>(localWidth, localHeight);

				CvInvoke.cvThreshold(localImage, processedLocalImage, ObjectThreshold, BackgroundColor, Emgu.CV.CvEnum.THRESH.CV_THRESH_BINARY);

				// Now both hand and trash are white.
				// Floodfill hand into black.
				MCvScalar black = new MCvScalar(255);
				MCvScalar objectThresholdScalar = new MCvScalar(ObjectThreshold);
				MCvConnectedComp comp = new MCvConnectedComp();

				for (int i = 0; i < processedLocalImage.Width; ++i)
					for (int j = 0; j < processedLocalImage.Height; ++j)
						if (depthPixels[i * stride + j * bytesPerPixel].PlayerIndex != 0)
							CvInvoke.cvFloodFill(processedLocalImage.Ptr, new System.Drawing.Point(i, j), black, objectThresholdScalar, objectThresholdScalar, out comp, 8, IntPtr.Zero);

				int midX = 0;
				int midY = 0;
				Gray white = new Gray(0);

				// Count the total white pixel number.
				int whitePixel = 0;
				for (int i = 0; i < processedLocalImage.Width; ++i)
					for (int j = 0; j < processedLocalImage.Height; ++j)
						if (Gray.Equals(processedLocalImage[i, j], white))
						{
							whitePixel++;
							trashFound = true;
						}

				int tempWhitePixel;

				// Find midX
				tempWhitePixel = 0;
				for (midX = 0; midX < processedLocalImage.Width; ++midX)
				{
					for (int j = 0; j < processedLocalImage.Height; ++j)
						if (Gray.Equals(processedLocalImage[midX, j], white))
							tempWhitePixel++;
					if (tempWhitePixel > whitePixel / 2)
						break;
				}

				// Find midY
				tempWhitePixel = 0;
				for (int i = 0; i < processedLocalImage.Width; ++i)
				{
					for (midY = 0; midY < processedLocalImage.Height; ++midY)
						if (Gray.Equals(processedLocalImage[i, midY], white))
							tempWhitePixel++;
					if (tempWhitePixel > whitePixel)
						break;
				}

				if (trashFound)
				{
					trashPoint.X = midX;
					trashPoint.Y = midY;
					trashPoint.Depth = depthPixels[midX * bytesPerPixel + midY * stride].Depth;
					return;
				}
			}
			trashFound = false;
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
			/*
			StringWriter stringWriter = new StringWriter();
			JsonWriter jsonWriter = new JsonTextWriter(stringWriter);

			jsonWriter.WriteStartObject();

			// Write the landing point
			jsonWriter.WritePropertyName("x");
			jsonWriter.WriteValue(landingPoint.X);
			jsonWriter.WritePropertyName("y");
			jsonWriter.WriteValue(landingPoint.Depth);

			jsonWriter.WriteEndObject();

			jsonWriter.Flush();

			string message = stringWriter.GetStringBuilder().ToString();
			*/

			string message = "{\"x\":" + landingPoint.X + ",\"y\":" + landingPoint.Depth + "}";

			server.Message = message;
		}

		private void SendLocationToBin(DepthImagePoint landingPoint, DepthImagePoint binPoint)
		{
			/*
			StringWriter stringWriter = new StringWriter();
			JsonWriter jsonWriter = new JsonTextWriter(stringWriter);

			jsonWriter.WriteStartObject();

			// Write the landing point
			jsonWriter.WritePropertyName("x");
			jsonWriter.WriteValue(landingPoint.X);
			jsonWriter.WritePropertyName("y");
			jsonWriter.WriteValue(landingPoint.Depth);

			// Write Automobin's location
			jsonWriter.WritePropertyName("binx");
			jsonWriter.WriteValue(binPoint.X);
			jsonWriter.WritePropertyName("biny");
			jsonWriter.WriteValue(binPoint.Depth);

			jsonWriter.WriteEndObject();

			jsonWriter.Flush();

			string message = stringWriter.GetStringBuilder().ToString();
			*/

			string message = "{\"x\":" + landingPoint.X + ",\"y\":" + landingPoint.Depth + ",\"binx\":" + binPoint.X + ",\"biny\":" + binPoint.Depth + "}";

			server.Message = message;
		}

		private void FindAutomobin(ref DepthImagePoint binPoint, ref bool binFound)
		{
			// Convert all the black pixels to pure green.
			Image<Bgr, Int32> tempImage = colorFrameImage.Copy();
			for(int i = 0; i < colorFrameImage.Width; ++i)
				for(int j = 0; j < colorFrameImage.Height; ++j)
					if(NearBlack(colorFrameImage[i, j]))
						tempImage[i, j] = new Bgr(0, 255, 0);

			// Floodfill and count the sizes of green areas
			// The largest one is Automobin
			Image<Gray, Int32> mask = new Image<Gray, Int32>(tempImage.Width + 2, tempImage.Height + 2);
			
			bool[,] visited = new bool[tempImage.Width, tempImage.Height];
			
			double maxArea = 0;
			int colorX = 0;
			int colorY = 0;

			for(int i = 0; i < tempImage.Width; ++i)
				for(int j = 0; j < tempImage.Height; ++j)
					if(tempImage[i, j].Green == 255)
					{
						MCvConnectedComp comp = new MCvConnectedComp();
						CvInvoke.cvFloodFill(
							tempImage,
							new System.Drawing.Point(i, j),
							new MCvScalar(255, 255, 255, 255),
							new MCvScalar(0),
							new MCvScalar(0),
							out comp,
							Emgu.CV.CvEnum.CONNECTIVITY.EIGHT_CONNECTED,
							Emgu.CV.CvEnum.FLOODFILL_FLAG.DEFAULT,
							mask);
						if(comp.area > maxArea)
						{
							maxArea = comp.area;
							colorX = comp.rect.X + comp.rect.Width / 2;
							colorY = comp.rect.Y + comp.rect.Height / 2;
						}
					}
			if(maxArea == 0)
			{
				binFound = false;
				return;
			}

			// Map color location to depth location
			DepthImagePoint[] depthPoints = new DepthImagePoint[tempImage.Width * tempImage.Height];
			sensor.CoordinateMapper.MapColorFrameToDepthFrame(sensor.ColorStream.Format, sensor.DepthStream.Format, depthPixels, depthPoints);

			long index = colorX * tempImage.Width + colorY;
			binPoint = depthPoints[index];
			binFound = true;
		}
		
		private bool NearBlack(Bgr color)
		{
			if ((255 - color.Blue <= BlackThreshold) &&
				(255 - color.Green <= BlackThreshold) &&
				(255 - color.Red <= BlackThreshold))
				return true;
			return false;
		}

		private bool AltDown = false;

		private void Window_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)
				AltDown = true;
			else if (e.SystemKey == Key.F4 && AltDown)
				e.Handled = true;
		}

		private void Window_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)
				AltDown = false;
		}
	}
}
