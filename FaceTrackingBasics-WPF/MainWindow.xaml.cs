// -----------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Documents;

namespace FaceTrackingBasics
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Windows.Controls;
    using System.Windows;
    using System.Windows.Data;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;
    using Microsoft.Kinect.Toolkit;
    using Microsoft.Speech.AudioFormat;
    using Microsoft.Speech.Recognition;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly int Bgr32BytesPerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7)/8;
        private readonly KinectSensorChooser sensorChooser = new KinectSensorChooser();
        private WriteableBitmap colorImageWritableBitmap;
        private byte[] colorImageData;
        private ColorImageFormat currentColorImageFormat = ColorImageFormat.Undefined;
        private int i = 0;
        private int logginNr = 1;
        private SpeechRecognitionEngine speechEngine;
        private string myPhotos;
        private string[] filesindirectory;
        private DateTime startTime = DateTime.UtcNow;
        private static Boolean PictureChanged = false;
        //gestures
        const int skeletonCount = 6;
        Skeleton[] allSkeletons = new Skeleton[skeletonCount];

        private enum Commands
        {
            Inn,
            Ut
        }

        private List<Span> recognitionSpans;


        public MainWindow()
        {
            InitializeComponent();

            var faceTrackingViewerBinding = new Binding("Kinect") {Source = sensorChooser};
            faceTrackingViewer.SetBinding(FaceTrackingViewer.KinectProperty, faceTrackingViewerBinding);

            sensorChooser.KinectChanged += SensorChooserOnKinectChanged;
            getDirectory();
          

            sensorChooser.Start();
        }

        private static RecognizerInfo GetKinectRecognizer()
        {
            foreach (var recognizer in SpeechRecognitionEngine.InstalledRecognizers())
            {
                string value;
                recognizer.AdditionalInfo.TryGetValue("Kinect", out value);
                if ("True".Equals(value, StringComparison.OrdinalIgnoreCase) &&
                    "en-Us".Equals(recognizer.Culture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return recognizer;
                }
            }
            return null;
        }


        private void SensorChooserOnKinectChanged(object sender, KinectChangedEventArgs kinectChangedEventArgs)
        {
            KinectSensor oldSensor = kinectChangedEventArgs.OldSensor;
            KinectSensor newSensor = kinectChangedEventArgs.NewSensor;

            if (oldSensor != null)
            {
                oldSensor.AllFramesReady -= KinectSensorOnAllFramesReady;
                oldSensor.ColorStream.Disable();
                oldSensor.DepthStream.Disable();
                oldSensor.DepthStream.Range = DepthRange.Default;
                oldSensor.SkeletonStream.Disable();
                oldSensor.SkeletonStream.EnableTrackingInNearRange = false;
                oldSensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
            }

            if (newSensor != null)
            {
                try
                {
                    newSensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                    newSensor.DepthStream.Enable(DepthImageFormat.Resolution320x240Fps30);
                    try
                    {
                        // This will throw on non Kinect For Windows devices.
                        newSensor.DepthStream.Range = DepthRange.Near;
                        newSensor.SkeletonStream.EnableTrackingInNearRange = true;
                    }
                    catch (InvalidOperationException)
                    {
                        newSensor.DepthStream.Range = DepthRange.Default;
                        newSensor.SkeletonStream.EnableTrackingInNearRange = false;
                    }

                    newSensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
                    newSensor.SkeletonStream.Enable();
                    newSensor.AllFramesReady += KinectSensorOnAllFramesReady;
                }
                catch (InvalidOperationException)
                {
                    // This exception can be thrown when we are trying to
                    // enable streams on a device that has gone away.  This
                    // can occur, say, in app shutdown scenarios when the sensor
                    // goes away between the time it changed status and the
                    // time we get the sensor changed notification.
                    //
                    // Behavior here is to just eat the exception and assume
                    // another notification will come along if a sensor
                    // comes back.
                }
            }
            RecognizerInfo ri = GetKinectRecognizer();
            if (ri != null)
            {


                speechEngine = new SpeechRecognitionEngine(ri);
                var directions = new Choices();
                directions.Add(new SemanticResultValue("inn", "INN"));
                directions.Add(new SemanticResultValue("ut", "UT"));

                var gb = new GrammarBuilder {Culture = ri.Culture};
                gb.Append(directions);
                var g = new Grammar(gb);
                speechEngine.LoadGrammar(g);

                speechEngine.SpeechRecognized += SpeechRecognized;
                speechEngine.SpeechRecognitionRejected += SpeechRejected;

                speechEngine.SetInputToAudioStream(newSensor.AudioSource.Start(),
                                                   new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2,
                                                                             null));
                speechEngine.RecognizeAsync(RecognizeMode.Multiple);

            }
            else
            {
                MessageBox.Show("No Recognizer");
            }

    

}

         private void WindowClosed(object sender, EventArgs e)
        {
            sensorChooser.Stop();
            faceTrackingViewer.Dispose();

            if (speechEngine != null)
            {
                speechEngine.SpeechRecognized -= SpeechRecognized;
                speechEngine.SpeechRecognitionRejected -= SpeechRejected;
                speechEngine.RecognizeAsyncStop();
            }
        }
        /// Remove any highlighting from recognition instructions.
//        private void ClearRecognitionHighlights()
//        {
//            foreach (Span span in recognitionSpans)
//            {
//                
//                span.FontWeight = FontWeights.Normal;
//            }
//        }

        /// Handler for recognized speech events.
        private void SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            const double ConfidenceThreshold = 0.4;
//            ClearRecognitionHighlights();
            if (e.Result.Confidence >= ConfidenceThreshold)
            {
               switch (e.Result.Semantics.Value.ToString())
               {
                   case"INN":
                       PersonInn();                    
                       break;
                   case"UT":
                       PersonUt();
                       break;
               } 
            }
        }
        private void SpeechRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
//            ClearRecognitionHighlights();
        }
        private void getDirectory()
        {
            myPhotos = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            filesindirectory = Directory.GetFiles(myPhotos, "*.png");
        }

   

        private void KinectSensorOnAllFramesReady(object sender, AllFramesReadyEventArgs allFramesReadyEventArgs)
        {
          

            using (var colorImageFrame = allFramesReadyEventArgs.OpenColorImageFrame())
            {
                if (colorImageFrame == null)
                {
                    return;
                }
                

                // Make a copy of the color frame for displaying.
                var haveNewFormat = this.currentColorImageFormat != colorImageFrame.Format;
                if (haveNewFormat)
                {
                    this.currentColorImageFormat = colorImageFrame.Format;
                    this.colorImageData = new byte[colorImageFrame.PixelDataLength];
                    this.colorImageWritableBitmap = new WriteableBitmap(
                        colorImageFrame.Width, colorImageFrame.Height, 96, 96, PixelFormats.Bgr32, null);
                    ColorImage.Source = this.colorImageWritableBitmap;
                }

                colorImageFrame.CopyPixelDataTo(this.colorImageData);
                this.colorImageWritableBitmap.WritePixels(
                    new Int32Rect(0, 0, colorImageFrame.Width, colorImageFrame.Height),
                    this.colorImageData,
                    colorImageFrame.Width * Bgr32BytesPerPixel,
                    0);
            }


            //_______SKeleton_________
            if (PictureChanged == false)
            {



                using (var skeletonFrame = allFramesReadyEventArgs.OpenSkeletonFrame())
                {


                    var obj = new object();
                    //Get a skeleton
                    Skeleton first = GetFirstSkeleton(allFramesReadyEventArgs);
                    if (first != null)
                    {

                        Monitor.Enter(obj);
                        try
                        {
                            ProcessGesture(
                            first.Joints[JointType.Head],
                            first.Joints[JointType.HandLeft],
                            first.Joints[JointType.HandRight],
                            first.Joints[JointType.HipCenter]

                            );
                        }
                        finally
                        {
                            Monitor.Exit(obj);
                        }
                    }
                }
            
            }
        }

 


        // Adding the Gestures
        private void ProcessGesture(Joint head, Joint handleft, Joint handright, Joint hipcenter)
        {
            
           
         
            if (handright.Position.Y > head.Position.Y&& PictureChanged==false)
            {
                
                Thread.Sleep(1000);
                NextPicture();

//                while (DateTime.UtcNow - startTime < TimeSpan.FromMilliseconds(1500))
//                {
//
//                }
                PictureChanged = false;
            }
            if (handleft.Position.Y > head.Position.Y && PictureChanged == false)
            {
                Thread.Sleep(1000);
                LastPicture();  
            }
            
          
            //if (handright.Position.X < hipcenter.Position.X)
            //{
            //    MessageBox.Show("Your right hand is over spine");
            //}

            // if (handleft.Position.X > hipcenter.Position.X)
            // {
            //     MessageBox.Show("Your left hand is over spine");
            // }

        }

        //Getting the first skeleton
        Skeleton GetFirstSkeleton(AllFramesReadyEventArgs e)
        {
            using (SkeletonFrame skeletonFrameData = e.OpenSkeletonFrame())
            {
                if (skeletonFrameData == null)
                {
                    return null;
                }


                skeletonFrameData.CopySkeletonDataTo(allSkeletons);

                //get the first tracked skeleton
                Skeleton first = (from s in allSkeletons
                                  where s.TrackingState == SkeletonTrackingState.Tracked
                                  select s).FirstOrDefault();

                return first;
            }
        }


 
        private void PersonUt()
        {
            ColorImage.Visibility = Visibility.Hidden;
           
           // string[] filesindirectory = Directory.GetFiles(@"C:\Users\RIKARD\Pictures\KinectBilder", "*.png");
            getDirectory();

            if (filesindirectory.Length == 0)
            {
                MessageBox.Show("No photos in folder.");
                return;
            }
            try
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(filesindirectory[0], UriKind.RelativeOrAbsolute);
                bi.EndInit();
                Bilde1.Source = bi;
                Bilde1.Visibility = Visibility.Visible;
                
            }
            catch (NotSupportedException)
            {
                Debug.WriteLine("Prøvde å lese noe annet enn en bildefil");
            }

            LoggUtKnapp.Visibility = Visibility.Hidden;
            LoggInnKnapp.Visibility = Visibility.Hidden;
            AvbrytButton.Visibility = Visibility.Visible;
            LoggMeOut.Visibility = Visibility.Visible;
            ForgjeButton.Visibility = Visibility.Visible;
            NesteButton.Visibility = Visibility.Visible;
        }



        private void PersonInn()
        {
            ColorImage.Visibility = Visibility.Visible;
            Bilde1.Visibility = Visibility.Hidden;
            if (sensorChooser.Kinect == null)
            {
                MessageBox.Show("Du må koble til en kinect enhet");
                return;
            }
            int colorWidth = sensorChooser.Kinect.ColorStream.FrameWidth;
            int colorHeight = sensorChooser.Kinect.ColorStream.FrameHeight;


            RenderTargetBitmap renderBitmap = new RenderTargetBitmap(colorWidth, colorHeight, 96.0, 96.0, PixelFormats.Pbgra32);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                VisualBrush facePhotoBrush = new VisualBrush(ColorImage);
                dc.DrawRectangle(facePhotoBrush, null, new Rect(new Point(), new Size(colorWidth, colorHeight)));

                //VisualBrush maskBrush = new VisualBrush(faceTrackingViewer);
                //dc.DrawRectangle(maskBrush, null, new Rect(new Point(FaceTrackingViewer.x, FaceTrackingViewer.y), new Size(faceWidth, faceHeight)));

            }
            renderBitmap.Render(dv);

            // create a png bitmap encoder which knows how to save a .png file
            BitmapEncoder encoder = new PngBitmapEncoder();

            // create frame from the writable bitmap and add to encoder
            encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
            string time = System.DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.CurrentUICulture.DateTimeFormat);

          

            //string path = Path.Combine(@"C:\Users\RIKARD\Pictures\KinectBilder", "Person logged inn @-" + time + ".png");

            string path = Path.Combine(myPhotos, "Person logged inn @-" + time + ".png");

            // write the new file to disk
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Create))
                {
                    encoder.Save(fs);
                }
            }
            catch (IOException)
            {
                MessageBox.Show("Saving of file failed.");
            }
            MyText.Text = "Loggin nr: " + logginNr;
            logginNr++;
        }
        private void NextPicture()
        {
            //var filesindirectory = Directory.GetFiles(@"C:\Users\RIKARD\Pictures\KinectBilder", "*.png");
            if (i == filesindirectory.Length - 1)
            {
                i = -1;
            }

            i++;


            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(filesindirectory[i], UriKind.RelativeOrAbsolute);
                bi.EndInit();
                Bilde1.Source = bi;
                Bilde1.Visibility = Visibility.Visible;
            }
            catch (NotSupportedException)
            {
                i = 0;
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(filesindirectory[i], UriKind.RelativeOrAbsolute);
                bi.EndInit();
                Bilde1.Source = bi;
                Bilde1.Visibility = Visibility.Visible;
                Debug.WriteLine("Prøvde å lese noe annet enn en bildefil");
            }
        }

        private void LastPicture()
        {
            // string[] filesindirectory = Directory.GetFiles(@"C:\Users\RIKARD\Pictures\KinectBilder", "*.png");
            if (i == 0)
            {
                //Må kanskje være -1
                i = filesindirectory.Length;
            }
            i--;
            try
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(filesindirectory[i], UriKind.RelativeOrAbsolute);
                bi.EndInit();
                Bilde1.Source = bi;
                Bilde1.Visibility = Visibility.Visible;
            }
            catch (NotSupportedException)
            {
                i = 0;
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(filesindirectory[i], UriKind.RelativeOrAbsolute);
                bi.EndInit();
                Bilde1.Source = bi;
                Bilde1.Visibility = Visibility.Visible;
                Debug.WriteLine("Prøvde å lese noe annet enn en bildefil");
            }
        }
        private void ButtonClicked(object sender, RoutedEventArgs e)
        {
            PersonInn();
        }
  

        private void LoggOutClicked(object sender, RoutedEventArgs e)
        {
            PersonUt();

        }

        private void NesteClicked(object sender, RoutedEventArgs e)
        {

            NextPicture();

        }

        private void LastClicked(object sender, RoutedEventArgs e)
        {
            LastPicture();
            
        }

        private void LoggMeOutClicked(object sender, RoutedEventArgs e)
        {

        }

        private void AvbrytClicked(object sender, RoutedEventArgs e)
        {
            LoggInnKnapp.Visibility = Visibility.Visible;
            LoggUtKnapp.Visibility = Visibility.Visible;
            ColorImage.Visibility = Visibility.Visible;


            Bilde1.Visibility = Visibility.Hidden;
            AvbrytButton.Visibility = Visibility.Hidden;
            LoggMeOut.Visibility = Visibility.Hidden;
            ForgjeButton.Visibility = Visibility.Hidden;
            NesteButton.Visibility = Visibility.Hidden;
            
        }
    }
}

