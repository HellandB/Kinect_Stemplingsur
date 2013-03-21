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
        private readonly int Bgr32BytesPerPixel;
        private readonly KinectSensorChooser sensorChooser;
        private WriteableBitmap ColorImageWritableBitmap;
        private byte[] ColorImageData;
        private ColorImageFormat CurrentColorImageFormat ;
        private int PictureNumber = 0;
        private int LogInNr = 1;
        private SpeechRecognitionEngine SpeechEngine;
        private string MyPhotos;
        private string[] FilesInDirectory;
        private Binding faceTrackingViewerBinding;
  
        //gestures
        const int skeletonCount = 6;
        Skeleton[] allSkeletons = new Skeleton[skeletonCount];

        private enum Commands
        {
            Inn,
            Ut
        }


        public MainWindow()
        {
            InitializeComponent();

            faceTrackingViewerBinding = new Binding("Kinect") {Source = sensorChooser};
            CurrentColorImageFormat = ColorImageFormat.Undefined;
            Bgr32BytesPerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;

            sensorChooser = new KinectSensorChooser();
            sensorChooser.KinectChanged += SensorChooserOnKinectChanged;
            sensorChooser.Start();

            faceTrackingViewer.SetBinding(FaceTrackingViewer.KinectProperty, faceTrackingViewerBinding);
            getDirectory();
           
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


                SpeechEngine = new SpeechRecognitionEngine(ri);
                var voiceCommands = new Choices();
                voiceCommands.Add(new SemanticResultValue("inn", "INN"));
                voiceCommands.Add(new SemanticResultValue("ut", "UT"));

                var gb = new GrammarBuilder {Culture = ri.Culture};
                gb.Append(voiceCommands);
                var g = new Grammar(gb);
                SpeechEngine.LoadGrammar(g);

                SpeechEngine.SpeechRecognized += SpeechRecognized;
                SpeechEngine.SpeechRecognitionRejected += SpeechRejected;

                SpeechEngine.SetInputToAudioStream(newSensor.AudioSource.Start(),
                                                   new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2,
                                                                             null));
                SpeechEngine.RecognizeAsync(RecognizeMode.Multiple);

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

            if (SpeechEngine != null)
            {
                SpeechEngine.SpeechRecognized -= SpeechRecognized;
                SpeechEngine.SpeechRecognitionRejected -= SpeechRejected;
                SpeechEngine.RecognizeAsyncStop();
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
                       PersonIn();                    
                       break;
                   case"UT":
                       PersonOut();
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
            MyPhotos = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            FilesInDirectory = Directory.GetFiles(MyPhotos, "*.png");
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
                var haveNewFormat = this.CurrentColorImageFormat != colorImageFrame.Format;
                if (haveNewFormat)
                {
                    this.CurrentColorImageFormat = colorImageFrame.Format;
                    this.ColorImageData = new byte[colorImageFrame.PixelDataLength];
                    this.ColorImageWritableBitmap = new WriteableBitmap(
                        colorImageFrame.Width, colorImageFrame.Height, 96, 96, PixelFormats.Bgr32, null);
                    ColorImage.Source = this.ColorImageWritableBitmap;
                }

                colorImageFrame.CopyPixelDataTo(this.ColorImageData);
                this.ColorImageWritableBitmap.WritePixels(
                    new Int32Rect(0, 0, colorImageFrame.Width, colorImageFrame.Height),
                    this.ColorImageData,
                    colorImageFrame.Width * Bgr32BytesPerPixel,
                    0);
            }


            //_______SKeleton_________
            //Only track skeleton when ColorImage is hidden.
            if (!ColorImage.IsVisible)
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


        // Processing the Gestures
        private void ProcessGesture(Joint head, Joint handleft, Joint handright, Joint hipcenter)
        {

            //if (handright.Position.Y > head.Position.Y)
            //{
            //    Thread.Sleep(1000);
            //    NextPicture();
            //    PictureChanged = false;
            //}

            //if (handleft.Position.Y > head.Position.Y )
            //{
            //    Thread.Sleep(1000);
            //    LastPicture();  
            //}

            if (handleft.Position.X > hipcenter.Position.X)
            { 
                Thread.Sleep(1000);
                PreviousPicture();
            }

            if (handright.Position.X < hipcenter.Position.X)
            {
                Thread.Sleep(1000);
                NextPicture();
            }
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


 
        private void PersonOut()
        {
            ColorImage.Visibility = Visibility.Hidden;
            InfoText.Text = "Bilde " + PictureNumber;

           // string[] filesindirectory = Directory.GetFiles(@"C:\Users\RIKARD\Pictures\KinectBilder", "*.png");
            getDirectory();

            PictureNumber = 0;

            if (FilesInDirectory.Length == 0)
            {
                MessageBox.Show("No photos in folder.");
                return;
            }
            try
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(FilesInDirectory[0], UriKind.RelativeOrAbsolute);
                bi.EndInit();
                PhotoFrame.Source = bi;
                PhotoFrame.Visibility = Visibility.Visible;
                
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



        private void PersonIn()
        {
            
            ColorImage.Visibility = Visibility.Visible;
            LoggInnKnapp.Visibility = Visibility.Visible;
            LoggUtKnapp.Visibility = Visibility.Visible;

            PhotoFrame.Visibility = Visibility.Hidden;
            AvbrytButton.Visibility = Visibility.Hidden;
            LoggMeOut.Visibility = Visibility.Hidden;
            ForgjeButton.Visibility = Visibility.Hidden;
            NesteButton.Visibility = Visibility.Hidden;

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

            string path = Path.Combine(MyPhotos, "Person logged inn @-" + time + ".png");

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
            InfoText.Text = "Loggin nr: " + LogInNr;
            LogInNr++;
        }
        private void NextPicture()
        {

            //var filesindirectory = Directory.GetFiles(@"C:\Users\RIKARD\Pictures\KinectBilder", "*.png");

            //Preventing PictureNumber outofbound
            if (PictureNumber == FilesInDirectory.Length-1)
            {
                PictureNumber = 0;
            }

            PictureNumber++;


            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(FilesInDirectory[PictureNumber], UriKind.RelativeOrAbsolute);
                bi.EndInit();
                PhotoFrame.Source = bi;
                PhotoFrame.Visibility = Visibility.Visible;
            }
            catch (NotSupportedException)
            {
                PictureNumber = 0;
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(FilesInDirectory[PictureNumber], UriKind.RelativeOrAbsolute);
                bi.EndInit();
                PhotoFrame.Source = bi;
                PhotoFrame.Visibility = Visibility.Visible;
                Debug.WriteLine("Prøvde å lese noe annet enn en bildefil");
            }
            InfoText.Text = "Neste bilde " + PictureNumber;
        }

        private void PreviousPicture()
        {

           
            // string[] filesindirectory = Directory.GetFiles(@"C:\Users\RIKARD\Pictures\KinectBilder", "*.png");
           
            //Preventing PictureNumber outofbound
            if (PictureNumber == 0)
            {
                //Må kanskje være -1
                PictureNumber = FilesInDirectory.Length;
            }

            PictureNumber--;
            try
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(FilesInDirectory[PictureNumber], UriKind.RelativeOrAbsolute);
                bi.EndInit();
                PhotoFrame.Source = bi;
                PhotoFrame.Visibility = Visibility.Visible;
            }
            catch (NotSupportedException)
            {
                PictureNumber = 0;
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(FilesInDirectory[PictureNumber], UriKind.RelativeOrAbsolute);
                bi.EndInit();
                PhotoFrame.Source = bi;
                PhotoFrame.Visibility = Visibility.Visible;
                Debug.WriteLine("Prøvde å lese noe annet enn en bildefil");
            }
            InfoText.Text = "Forrige bilde " + PictureNumber;
        }
        private void ButtonClicked(object sender, RoutedEventArgs e)
        {
            PersonIn();
        }
  

        private void LoggOutClicked(object sender, RoutedEventArgs e)
        {
            PersonOut();

        }

        private void NesteClicked(object sender, RoutedEventArgs e)
        {

            NextPicture();

        }

        private void LastClicked(object sender, RoutedEventArgs e)
        {
            PreviousPicture();
            
        }

        private void LoggMeOutClicked(object sender, RoutedEventArgs e)
        {

        }

        private void AvbrytClicked(object sender, RoutedEventArgs e)
        {
            InfoText.Text = "Inn";
            PictureNumber = 0;
            LoggInnKnapp.Visibility = Visibility.Visible;
            LoggUtKnapp.Visibility = Visibility.Visible;
            ColorImage.Visibility = Visibility.Visible;
            

            PhotoFrame.Visibility = Visibility.Hidden;
            AvbrytButton.Visibility = Visibility.Hidden;
            LoggMeOut.Visibility = Visibility.Hidden;
            ForgjeButton.Visibility = Visibility.Hidden;
            NesteButton.Visibility = Visibility.Hidden;
            
        }
    }
}

