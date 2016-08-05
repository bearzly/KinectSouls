using Microsoft.Kinect.Toolkit;
using Microsoft.Kinect;
using System;
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
using Microsoft.Kinect.Toolkit.Controls;
using Microsoft.Kinect.Toolkit.BackgroundRemoval;
using Microsoft.Kinect.Toolkit.Interaction;

namespace KinectSouls
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly KinectSensorChooser sensorChooser;

        private BackgroundRemovedColorStream colorStream;

        private WriteableBitmap foregroundBitmap;

        private InteractionStream interactionStream;

        private Skeleton[] skeletons;
        private UserInfo[] userInfos;

        private VirtualController controller;

        class DummyInteractionClient : IInteractionClient
        {
            public InteractionInfo GetInteractionInfoAtLocation(int skeletonTrackingId, InteractionHandType handType, double x, double y)
            {
                var result = new InteractionInfo();
                result.IsGripTarget = true;
                result.IsPressTarget = true;
                result.PressAttractionPointX = 0.5;
                result.PressAttractionPointY = 0.5;
                result.PressTargetControlId = 1;
                return result;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            this.controller = new VirtualController();

            this.sensorChooser = new KinectSensorChooser();
            this.sensorChooser.KinectChanged += this.SensorChooserOnKinectChanged;
            this.sensorChooserUi.KinectSensorChooser = this.sensorChooser;
            this.sensorChooser.Start();

            var regionSensorBinding = new Binding("Kinect") { Source = this.sensorChooser };
            BindingOperations.SetBinding(this.kinectRegion, KinectRegion.KinectSensorProperty, regionSensorBinding);
        }

        private void SensorChooserOnKinectChanged(object sender, KinectChangedEventArgs args)
        {
            if (args.OldSensor != null)
            {
                try
                {
                    args.OldSensor.AllFramesReady -= this.SensorAllFramesReady;
                    args.OldSensor.DepthStream.Range = DepthRange.Default;
                    args.OldSensor.SkeletonStream.EnableTrackingInNearRange = false;
                    args.OldSensor.DepthStream.Disable();
                    args.OldSensor.ColorStream.Disable();
                    args.OldSensor.SkeletonStream.Disable();

                    if (colorStream != null)
                    {
                        this.colorStream.BackgroundRemovedFrameReady -= this.BackgroundRemovedFrameReadyHandler;
                        this.colorStream.Dispose();
                        this.colorStream = null;
                    }

                    if (interactionStream != null)
                    {
                        this.interactionStream.InteractionFrameReady -= this.InteractionFrameReadyHandler;
                        this.interactionStream.Dispose();
                        this.interactionStream = null;
                    }
                    this.controller.Sensor = null;
                }
                catch (InvalidOperationException)
                {
                    // the sample says something bad might happen while trying to do that stuff
                }
            }
            if (args.NewSensor != null)
            {
                try
                {
                    TransformSmoothParameters smoothingParam = new TransformSmoothParameters();
                    {
                        smoothingParam.Smoothing = 0.5f;
                        smoothingParam.Correction = 0.5f;
                        smoothingParam.Prediction = 0.5f;
                        smoothingParam.JitterRadius = 0.05f;
                        smoothingParam.MaxDeviationRadius = 0.04f;
                    };

                    args.NewSensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                    args.NewSensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                    args.NewSensor.SkeletonStream.Enable(smoothingParam);
                    args.NewSensor.SkeletonStream.Enable();
                    args.NewSensor.DepthStream.Range = DepthRange.Default;
                    args.NewSensor.SkeletonStream.EnableTrackingInNearRange = false;

                    this.skeletons = new Skeleton[args.NewSensor.SkeletonStream.FrameSkeletonArrayLength];

                    this.colorStream = new BackgroundRemovedColorStream(args.NewSensor);
                    this.colorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30, DepthImageFormat.Resolution640x480Fps30);
                    this.colorStream.BackgroundRemovedFrameReady += this.BackgroundRemovedFrameReadyHandler;

                    this.interactionStream = new InteractionStream(args.NewSensor, new DummyInteractionClient());
                    this.interactionStream.InteractionFrameReady += this.InteractionFrameReadyHandler;

                    args.NewSensor.AllFramesReady += this.SensorAllFramesReady;

                    this.controller.Sensor = args.NewSensor;
                }
                catch (InvalidOperationException)
                {
                    // I guess something might go wrong
                }
            }
        }

        private void InteractionFrameReadyHandler(object sender, InteractionFrameReadyEventArgs e)
        {
            using (var interactionFrame = e.OpenInteractionFrame())
            {
                if (interactionFrame != null)
                {
                    if (this.userInfos == null)
                    {
                        this.userInfos = new UserInfo[InteractionFrame.UserInfoArrayLength];
                    }
                    interactionFrame.CopyInteractionDataTo(this.userInfos);
                    this.controller.processInteractionInfo(this.userInfos, interactionFrame.Timestamp);
                }
            }
        }

        private void SensorAllFramesReady(object sender, AllFramesReadyEventArgs e)
        {
            // in the middle of shutting down, or lingering events from previous sensor, do nothing here.
            if (null == this.sensorChooser || null == this.sensorChooser.Kinect || this.sensorChooser.Kinect != sender)
            {
                return;
            }

            try
            {
                using (var depthFrame = e.OpenDepthImageFrame())
                {
                    if (null != depthFrame)
                    {
                        this.colorStream.ProcessDepth(depthFrame.GetRawPixelData(), depthFrame.Timestamp);
                        this.interactionStream.ProcessDepth(depthFrame.GetRawPixelData(), depthFrame.Timestamp);
                    }
                }

                using (var colorFrame = e.OpenColorImageFrame())
                {
                    if (null != colorFrame)
                    {
                        this.colorStream.ProcessColor(colorFrame.GetRawPixelData(), colorFrame.Timestamp);
                    }
                }

                using (var skeletonFrame = e.OpenSkeletonFrame())
                {
                    if (null != skeletonFrame)
                    {
                        skeletonFrame.CopySkeletonDataTo(this.skeletons);
                        this.colorStream.ProcessSkeleton(this.skeletons, skeletonFrame.Timestamp);
                        this.interactionStream.ProcessSkeleton(this.skeletons, this.sensorChooser.Kinect.AccelerometerGetCurrentReading(), skeletonFrame.Timestamp);
                        this.controller.processSkeletons(this.skeletons, skeletonFrame.Timestamp);
                    }
                }

                var skeleton = skeletons.FirstOrDefault(x => x != null && x.TrackingState == SkeletonTrackingState.Tracked);
                if (skeleton != null)
                {
                    colorStream.SetTrackedPlayer(skeleton.TrackingId);
                }
            }
            catch (InvalidOperationException)
            {
                // Ignore the exception. 
            }
        }

        private void BackgroundRemovedFrameReadyHandler(object sender, BackgroundRemovedColorFrameReadyEventArgs e)
        {
            return;
            using (var backgroundRemovedFrame = e.OpenBackgroundRemovedColorFrame())
            {
                if (backgroundRemovedFrame != null)
                {
                    if (null == this.foregroundBitmap || this.foregroundBitmap.PixelWidth != backgroundRemovedFrame.Width
                        || this.foregroundBitmap.PixelHeight != backgroundRemovedFrame.Height)
                    {
                        this.foregroundBitmap = new WriteableBitmap(backgroundRemovedFrame.Width, backgroundRemovedFrame.Height, 96.0, 96.0, PixelFormats.Bgra32, null);

                        // Set the image we display to point to the bitmap where we'll put the image data
                        this.imageTarget.Source = this.foregroundBitmap;
                    }

                    // Write the pixel data into our bitmap
                    this.foregroundBitmap.WritePixels(
                        new Int32Rect(0, 0, this.foregroundBitmap.PixelWidth, this.foregroundBitmap.PixelHeight),
                        backgroundRemovedFrame.GetRawPixelData(),
                        this.foregroundBitmap.PixelWidth * sizeof(int),
                        0);
                }
            }
        }

        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.sensorChooser.Stop();
        }

        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            DrawingGroup g = new DrawingGroup();
            DrawingImage im = new DrawingImage(g);
            debugImage.Source = im;
            this.controller.Drawing = g;
        }
    }
}
