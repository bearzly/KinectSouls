// -----------------------------------------------------------------------
// <copyright file="DepthImageProcessor.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace KinectSouls
{
    using System;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    using Microsoft.Kinect;
    using Microsoft.Kinect.Toolkit.Controls;

    using Emgu.CV;
    using Emgu.CV.CvEnum;
    using Emgu.CV.Structure;

    /// <summary>
    /// Used to process/render a depth bitmap efficiently.
    /// </summary>
    internal class DepthImageProcessor : DependencyObject, IDisposable
    {
        public static readonly DependencyProperty KinectSensorProperty = DependencyProperty.Register(
            "KinectSensor",
            typeof(KinectSensor),
            typeof(DepthImageProcessor),
            new FrameworkPropertyMetadata(null, (source, e) => ((DepthImageProcessor)source).KinectSensorPropertyChanged(e.OldValue as KinectSensor, e.NewValue as KinectSensor)));

        public static readonly DependencyProperty KinectRegionProperty = DependencyProperty.Register(
            "KinectRegion",
            typeof(KinectRegion),
            typeof(DepthImageProcessor),
            new FrameworkPropertyMetadata(
                null,
                (source, e) =>
                ((DepthImageProcessor)source).KinectRegionPropertyChanged(e.NewValue as KinectRegion)));

        public static readonly DependencyProperty WriteableBitmapProperty = DependencyProperty.Register(
            "WriteableBitmap",
            typeof(WriteableBitmap),
            typeof(DepthImageProcessor));

        private DepthImagePixel[] depthBuffer;
        private byte[] colorizedDepthBuffer;
        private uint[] userColorLookupTable;

        /// <summary>
        /// Initializes a new instance of the DepthImageProcessor class.
        /// </summary>
        public DepthImageProcessor()
        {
            this.userColorLookupTable = new uint[] { 0, 0xff00ff00 };
        }

        public event EventHandler<DepthImageProcessedEventArgs> ProcessedDepthImageReady;

        public KinectSensor KinectSensor
        {
            get { return (KinectSensor)this.GetValue(KinectSensorProperty); }
            set { this.SetValue(KinectSensorProperty, value); }
        }

        public KinectRegion KinectRegion
        {
            get { return (KinectRegion)this.GetValue(KinectRegionProperty); }
            set { this.SetValue(KinectRegionProperty, value); }
        }

        public int TargetWidth { get; set; }

        public int TargetHeight { get; set; }

        public WriteableBitmap WriteableBitmap
        {
            get { return (WriteableBitmap)this.GetValue(WriteableBitmapProperty); }
            private set { this.SetValue(WriteableBitmapProperty, value); }
        }

        public void Dispose()
        {
            this.KinectSensorPropertyChanged(KinectSensor, null);
        }

        private void ColorizeDepthPixels(DepthImagePixel[] depthImagePixels, byte[] colorBuffer, int depthWidth, int depthHeight, int downscaleFactor)
        {
            int pixelDisplacementBetweenRows = depthWidth * downscaleFactor;

            unsafe
            {
                fixed (byte* colorBufferPtr = colorBuffer)
                {
                    int rows = 0;

                    fixed (DepthImagePixel* depthImagePixelPtr = depthImagePixels)
                    {
                        fixed (uint* playerColorLookupPtr = this.userColorLookupTable)
                        {
                            // Write color values using int pointers instead of byte pointers,
                            // since each color pixel is 32-bits wide.
                            uint* colorBufferIntPtr = (uint*)colorBufferPtr;
                            DepthImagePixel* currentPixelRowPtr = depthImagePixelPtr;

                            for (int row = 0; row < depthHeight; row += downscaleFactor)
                            {
                                DepthImagePixel* currentPixelPtr = currentPixelRowPtr;
                                for (int column = 0; column < depthWidth; column += downscaleFactor)
                                {
                                    if (currentPixelPtr->PlayerIndex == 0)
                                    {
                                        *colorBufferIntPtr++ = 0;
                                    }
                                    else
                                    {
                                        *colorBufferIntPtr++ = 0x000000ff;
                                    }
                                    currentPixelPtr += downscaleFactor;
                                }

                                currentPixelRowPtr += pixelDisplacementBetweenRows;
                                rows++;
                            }
                        }
                    }

                    int cols = depthWidth / downscaleFactor;

                    using (Mat image = new Mat(new System.Drawing.Size(cols, rows), DepthType.Cv8U, 4, new IntPtr(colorBufferPtr), cols * 4),
                           gray = new Mat(), blurred = new Mat(), cleaned = new Mat())
                    {
                        CvInvoke.CvtColor(image, gray, ColorConversion.Bgra2Gray);
                        CvInvoke.GaussianBlur(gray, blurred, new System.Drawing.Size(9, 9), 0);
                        CvInvoke.Threshold(blurred, cleaned, 127, 255, ThresholdType.Binary);

                        using (Image<Gray, byte> grayscale = blurred.ToImage<Gray, byte>())
                        {
                            fixed (byte* grayPtr = grayscale.Data)
                            {
                                byte* grayPtrCopy = grayPtr;
                                uint* colorBufferIntPtr = (uint*)colorBufferPtr;
                                for (int row = 0; row < depthHeight; row += downscaleFactor)
                                {
                                    for (int col = 0; col < depthWidth; col += downscaleFactor)
                                    {
                                        byte grayValue = *grayPtrCopy++;
                                        uint c = 0xff000000;
                                        c|= (uint)(grayValue << 16 | grayValue << 8 | grayValue);
                                        *colorBufferIntPtr++ = (grayValue == 0) ? 0 : c;
                                    }
                                }
                            }
                        }

                    }
                }
            }
        }

        /// <summary>
        /// Process a frame and write it to the bitmap.
        /// </summary>
        public void WriteToBitmap(DepthImageFrame frame)
        {
            if ((null == this.depthBuffer) || (this.depthBuffer.Length != frame.PixelDataLength))
            {
                this.depthBuffer = new DepthImagePixel[frame.PixelDataLength];
                this.colorizedDepthBuffer = new byte[frame.PixelDataLength * 4];
            }

            if (null == WriteableBitmap || WriteableBitmap.Format != PixelFormats.Bgra32)
            {
                this.CreateWriteableBitmap(frame);
            }

            this.depthBuffer = frame.GetRawPixelData();

            this.ColorizeDepthPixels(this.depthBuffer, this.colorizedDepthBuffer, frame.Width, frame.Height, (int)(frame.Width / WriteableBitmap.Width));

            this.WriteableBitmap.WritePixels(
                new Int32Rect(0, 0, WriteableBitmap.PixelWidth, WriteableBitmap.PixelHeight),
                this.colorizedDepthBuffer,
                (int)(WriteableBitmap.Width * 4),
                0);

            this.SendDepthImageReady(this.WriteableBitmap);
        }

        private static void EnforceAspectRatio(double targetAspectRatio, ref int width, ref int height)
        {
            var aspectRatio = (double)width / height;

            if (aspectRatio > targetAspectRatio)
            {
                // Input is too wide.
                width = (int)(height * aspectRatio);
            }
            else
            {
                // Input is too long.
                height = (int)(width / targetAspectRatio);
            }
        }

        private static void FindNextLargestFrameDimensions(DepthImageFrame frame, int targetWidth, int targetHeight, out int width, out int height)
        {
            width = frame.Width;
            height = frame.Height;

            if (frame.Width < targetWidth && frame.Height < targetHeight)
            {
                return;
            }

            while (width >= targetWidth * 2 && height >= targetHeight * 2 && width % 2 == 0 && height % 2 == 0)
            {
                width /= 2;
                height /= 2;
            }
        }

        private void CreateWriteableBitmap(DepthImageFrame frame)
        {
            int fixedTargetWidth = this.TargetWidth;
            int fixedTargetHeight = this.TargetHeight;

            int finalWidth;
            int finalHeight;

            EnforceAspectRatio((double)frame.Width / frame.Height, ref fixedTargetWidth, ref fixedTargetHeight);
            FindNextLargestFrameDimensions(frame, fixedTargetWidth, fixedTargetHeight, out finalWidth, out finalHeight);

            WriteableBitmap = new WriteableBitmap(
                finalWidth,
                finalHeight,
                96,
                96,
                PixelFormats.Bgra32,
                null);
        }

        private void KinectSensorPropertyChanged(KinectSensor oldSensor, KinectSensor newSensor)
        {
            if (oldSensor != null)
            {
                oldSensor.DepthFrameReady -= this.KinectSensorOnDepthFrameReady;
            }

            if (newSensor != null)
            {
                newSensor.DepthFrameReady += this.KinectSensorOnDepthFrameReady;
            }

            this.SendDepthImageReady(null);
        }

        private void ColorizeBackground()
        {
            unsafe
            {
                fixed (byte* colorBufferPtr = this.colorizedDepthBuffer)
                {
                    // Write color values using int pointers instead of byte pointers,
                    // since each color pixel is 32-bits wide.
                    uint* colorBufferIntPtr = (uint*)colorBufferPtr;

                    for (int pixel = 0; pixel < this.colorizedDepthBuffer.Length; pixel += 4)
                    {
                        *colorBufferIntPtr++ = 0xffffffff;
                    }
                }
            }
        }

        private void KinectRegionPropertyChanged(KinectRegion newRegion)
        {
            if ((newRegion == null) && (this.colorizedDepthBuffer != null) && (this.WriteableBitmap != null))
            {
                // Clear color image to background color for as long as KinectRegion is invalid
                this.ColorizeBackground();
                this.WriteableBitmap.WritePixels(
                    new Int32Rect(0, 0, WriteableBitmap.PixelWidth, WriteableBitmap.PixelHeight),
                    this.colorizedDepthBuffer,
                    (int)(WriteableBitmap.Width * 4),
                    0);

                this.SendDepthImageReady(this.WriteableBitmap);
            }
        }

        private void KinectSensorOnDepthFrameReady(object sender, DepthImageFrameReadyEventArgs depthImageFrameReadyEventArgs)
        {
            if ((this.KinectSensor != null) && (this.KinectRegion != null))
            {
                using (var frame = depthImageFrameReadyEventArgs.OpenDepthImageFrame())
                {
                    if (frame != null)
                    {
                        try
                        {
                            this.WriteToBitmap(frame);
                        }
                        catch (InvalidOperationException)
                        {
                            // DepthFrame functions may throw when the sensor gets
                            // into a bad state.  Ignore the frame in that case.
                        }
                    }
                }
            }
        }

        private void SendDepthImageReady(WriteableBitmap writeableBitmap)
        {
            if (this.ProcessedDepthImageReady != null)
            {
                this.ProcessedDepthImageReady(this, new DepthImageProcessedEventArgs { OutputBitmap = writeableBitmap });
            }
        }
    }

    internal class DepthImageProcessedEventArgs : EventArgs
    {
        public WriteableBitmap OutputBitmap { get; set; }
    }
}