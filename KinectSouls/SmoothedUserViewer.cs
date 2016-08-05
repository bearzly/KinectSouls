// -----------------------------------------------------------------------
// <copyright file="KinectUserViewer.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace KinectSouls
{
    using Microsoft.Kinect;
    using Microsoft.Kinect.Toolkit.Controls;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    /// <summary>
    /// Framework element that displays Kinect depth data colorized by user tracking ID or primary vs non-primary status.
    /// </summary>
    public class SmoothedUserViewer : FrameworkElement
    {
        public static readonly DependencyProperty ImageBackgroundProperty = DependencyProperty.Register(
            "ImageBackground",
            typeof(Brush),
            typeof(SmoothedUserViewer),
            new FrameworkPropertyMetadata(null, (o, e) => ((SmoothedUserViewer)o).InvalidateVisual()));

        /// <summary>
        /// Natural width of the KinectUserViewer in pixels.
        /// </summary>
        private const double NaturalWidth = 128;

        /// <summary>
        /// Natural height of the KinectUserViewer in pixels.
        /// </summary>
        private const double NaturalHeight = 96;

        /// <summary>
        /// Smallest such that 1.0+DoubleEpsilon != 1.0
        /// </summary>
        private const double DoubleEpsilon = 2.2204460492503131e-016;

        /// <summary>
        /// Binds KinectRegion to KinectUserViewer.
        /// </summary>
        private readonly KinectRegionBinder kinectRegionBinder;

        /// <summary>
        /// Internal depth image processor.
        /// </summary>
        private DepthImageProcessor depthImageProcessor;

        /// <summary>
        /// Writeable bitmap used to render the depth image.
        /// </summary>
        private WriteableBitmap writeableBitmap;



        [SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline", Justification = "We need to OverrideMetadata in the static constructor")]
        static SmoothedUserViewer()
        {
            // Set default style key to be this control type
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SmoothedUserViewer), new FrameworkPropertyMetadata(typeof(SmoothedUserViewer)));

            // Set default style to have FlowDirection be LeftToRight
            var style = new Style(typeof(SmoothedUserViewer), null);
            style.Setters.Add(new Setter(FlowDirectionProperty, FlowDirection.LeftToRight));
            style.Seal();
            StyleProperty.OverrideMetadata(typeof(SmoothedUserViewer), new FrameworkPropertyMetadata(style));
        }

        public SmoothedUserViewer()
        {
            // Create KinectRegion binding
            this.kinectRegionBinder = new KinectRegionBinder(this);
            this.kinectRegionBinder.OnKinectRegionChanged += this.OnKinectRegionChanged;
            this.kinectRegionBinder.OnKinectSensorChanged += this.OnKinectSensorChanged;
        }

        /// <summary>
        /// The KinectSensor supplying depth image data.
        /// </summary>
        public KinectSensor KinectSensor
        {
            get { return this.depthImageProcessor.KinectSensor; }
            set { this.depthImageProcessor.KinectSensor = value; }
        }

        /// <summary>
        /// Image background brush.
        /// </summary>
        public Brush ImageBackground
        {
            get { return (Brush)this.GetValue(ImageBackgroundProperty); }
            set { this.SetValue(ImageBackgroundProperty, value); }
        }

        /// <summary>
        /// Updates DesiredSize of KinectUserViewer.
        /// </summary>
        /// <param name="availableSize">The "upper limit" that the KinectUserViewer should not exceed.</param>
        /// <returns>KinectUserViewer's desired size.</returns>
        protected override Size MeasureOverride(Size availableSize)
        {
            return MeasureArrangeHelper(availableSize);
        }

        /// <summary>
        /// Override for ArrangeOverride.
        /// </summary>
        /// <param name="finalSize">The final size that the KinectUserViewer is being given.</param>
        /// <returns>The final size the KinectUserViewer needs.</returns>
        protected override Size ArrangeOverride(Size finalSize)
        {
            return MeasureArrangeHelper(finalSize);
        }

        /// <summary>
        /// Draw the image from the bitmap.
        /// </summary>
        protected override void OnRender(DrawingContext drawingContext)
        {
            if (drawingContext == null)
            {
                throw new ArgumentNullException("drawingContext");
            }

            base.OnRender(drawingContext);

            var rectangle = new Rect(new Point(), RenderSize);
            drawingContext.DrawRectangle(this.ImageBackground, null, rectangle);

            if (this.writeableBitmap != null)
            {
                drawingContext.DrawImage(this.writeableBitmap, rectangle);
            }
        }

        /// <summary>
        /// Create new DepthImageProcessor if render size was changed.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Bug in code analysis.  The depthImageProcessor gets disposed elsewhere.")]
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            if (sizeInfo == null)
            {
                throw new ArgumentNullException("sizeInfo");
            }

            base.OnRenderSizeChanged(sizeInfo);

            if (sizeInfo.HeightChanged || sizeInfo.WidthChanged)
            {
                if (this.depthImageProcessor != null)
                {
                    this.depthImageProcessor.ProcessedDepthImageReady -= this.OnDepthImageProcessorImageReady;
                    this.depthImageProcessor.Dispose();
                }

                this.depthImageProcessor = new DepthImageProcessor
                {
                    TargetWidth = (int)sizeInfo.NewSize.Width,
                    TargetHeight = (int)sizeInfo.NewSize.Height,
                    KinectRegion = this.kinectRegionBinder.KinectRegion,
                    KinectSensor = this.kinectRegionBinder.KinectSensor
                };

                this.depthImageProcessor.ProcessedDepthImageReady += this.OnDepthImageProcessorImageReady;
            }
        }

        /// <summary>
        /// Helper function that computes scale factors depending on target size and content size.
        /// </summary>
        /// <param name="availableSize">Size into which the content is being fitted.</param>
        /// <param name="contentSize">Size of the content measured unconstrained.</param>
        /// <returns>The scale factor to apply.</returns>
        private static Size ComputeScaleFactor(Size availableSize, Size contentSize)
        {
            // Compute scaling factors to use for axes
            double scaleX = 1.0;
            double scaleY = 1.0;

            bool isConstrainedWidth = !double.IsPositiveInfinity(availableSize.Width);
            bool isConstrainedHeight = !double.IsPositiveInfinity(availableSize.Height);

            if (isConstrainedWidth || isConstrainedHeight)
            {
                // Compute scaling factors for both axes
                scaleX = (contentSize.Width < 10.0 * DoubleEpsilon) ? 0.0 : availableSize.Width / contentSize.Width;
                scaleY = (contentSize.Height < 10.0 * DoubleEpsilon) ? 0.0 : availableSize.Height / contentSize.Height;

                if (!isConstrainedWidth)
                {
                    scaleX = scaleY;
                }
                else if (!isConstrainedHeight)
                {
                    scaleY = scaleX;
                }
                else
                {
                    double minscale = scaleX < scaleY ? scaleX : scaleY;
                    scaleX = scaleY = minscale;
                }
            }

            // Return this as a size now.
            return new Size(scaleX, scaleY);
        }

        private static Size MeasureArrangeHelper(Size inputSize)
        {
            // Set the natural size
            var naturalSize = new Size(NaturalWidth, NaturalHeight);

            // Get computed scale factor 
            var scaleFactor = ComputeScaleFactor(inputSize, naturalSize);

            // Returns our minimum size & sets DesiredSize.
            return new Size(naturalSize.Width * scaleFactor.Width, naturalSize.Height * scaleFactor.Height);
        }

        /// <summary>
        /// DepthImageProcessor handler.
        /// </summary>
        private void OnDepthImageProcessorImageReady(object sender, DepthImageProcessedEventArgs e)
        {
            this.writeableBitmap = e.OutputBitmap;
            this.InvalidateVisual();
        }

        /// <summary>
        /// KinectRegion changed handler.
        /// </summary>
        private void OnKinectRegionChanged(object sender, KinectRegion oldRegion, KinectRegion newRegion)
        {
            if (this.depthImageProcessor != null)
            {
                this.depthImageProcessor.KinectRegion = newRegion;
            }
        }

        /// <summary>
        /// KinectSensor changed handler.
        /// </summary>
        private void OnKinectSensorChanged(object sender, KinectSensor oldSensor, KinectSensor newSensor)
        {
            if (this.depthImageProcessor != null)
            {
                this.depthImageProcessor.KinectSensor = newSensor;
            }
        }
    }
}
