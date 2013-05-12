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
        private int LoginNr = 1;
        private SpeechRecognitionEngine SpeechEngine;
        private string MyPhotos;
        private string[] FilesInDirectory;
        private Binding faceTrackingViewerBinding;
        private Boolean ableToRemove = false;
  
        //gestures
        const int skeletonCount = 6;
        Skeleton[] allSkeletons = new Skeleton[skeletonCount];

        private enum Commands
        {
            Inn,
            Ut,
            Slett
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
            GetDirectory();
           
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

                    newSensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
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
                voiceCommands.Add(new SemanticResultValue("slett", "SLETT"));

                var gb = new GrammarBuilder {Culture = ri.Culture};
                gb.Append(voiceCommands);
                var g = new Grammar(gb);
                SpeechEngine.LoadGrammar(g);

                SpeechEngine.SpeechRecognized += SpeechRecognized;
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
                SpeechEngine.RecognizeAsyncStop();
            }
        }


        /// Handler for recognized speech events.
        private void SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            const double confidenceThreshold = 0.7;
            if (e.Result.Confidence >= confidenceThreshold)
            {
               switch (e.Result.Semantics.Value.ToString())
               {
                   case"INN":
                       PersonIn();                    
                       break;
                   case"UT":
                       PersonOut();
                       break;
                   case"SLETT":
                       if(ableToRemove)
                       CopyPicture(FilesInDirectory[PictureNumber]);
                       break;
               } 
            }
        }

        private void GetDirectory()
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


            //_______Skeleton_________
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
            ableToRemove = true;
            ColorImage.Visibility = Visibility.Hidden;
            InfoText.Text = "Bilde " + PictureNumber;  
            GetDirectory();

            PictureNumber = 0;

            if (FilesInDirectory.Length == 0)
            {
                MessageBox.Show("No photos in folder.");
                return;
            }
            try
            {
                var bi = new BitmapImage();
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
            AvbrytButton.Visibility = Visibility.Hidden;
            LogMeOut.Visibility = Visibility.Hidden;
            ForrigeButton.Visibility = Visibility.Hidden;
            NesteButton.Visibility = Visibility.Hidden;
        }



        private void PersonIn()
        {
            ableToRemove = false;
            ColorImage.Visibility = Visibility.Visible;
            LoggInnKnapp.Visibility = Visibility.Hidden;
            LoggUtKnapp.Visibility = Visibility.Hidden;

            PhotoFrame.Visibility = Visibility.Hidden;
            AvbrytButton.Visibility = Visibility.Hidden;
            LogMeOut.Visibility = Visibility.Hidden;
            ForrigeButton.Visibility = Visibility.Hidden;
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

            }
            renderBitmap.Render(dv);

            // create a png bitmap encoder which knows how to save a .png file
            BitmapEncoder encoder = new PngBitmapEncoder();

            // create frame from the writable bitmap and add to encoder
            encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
            string time = System.DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.CurrentUICulture.DateTimeFormat);
            string path = Path.Combine(MyPhotos, "loggin@-" + time + ".png");

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
            InfoText.Text = "Loggin nr: " + LoginNr;
            LoginNr++;
        }
        public void CopyPicture(String fileString)
        {

            string sourceFile = fileString;
            int endLength = sourceFile.Length - 25;
            string FileName = sourceFile.Substring(25, endLength);

            if (!Directory.Exists(MyPhotos + @"\LoggedOut\"))
            {
                Directory.CreateDirectory(MyPhotos + @"\LoggedOut\");
            }
         
            string destinationFile = MyPhotos + @"\LoggedOut\" + FileName;

            // To copy a file to another location and  
            // overwrite the destination file if it already exists. 
            try
            {
                File.Copy(sourceFile, destinationFile, true);
                InfoText.Text = "Ha en god dag! " + FileName;

                
            }
            catch (IOException)
            {
                Console.WriteLine("Kopiering feilet!");
               
            }
            ShowStartScreen();



        } 

        public void DeletePicture(String fileString)
        {
  
            // Delete a file by using File class static method... 
            if (File.Exists(fileString))
                // Use a try block to catch IOExceptions, to 
                // handle the case of the file already being 
                // opened by another process. 
                Console.WriteLine("Bildet finnes");
                try
                {
                   
                    File.Delete(fileString);
                    Console.WriteLine("Bildet ble slettet: " + fileString);
                    
                    
                }
                catch (System.IO.IOException e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine("Bildet ble ikke slettet: ");
                    return;
                }
        }
        private void ShowStartScreen()
        {
            ableToRemove = false;
            PictureNumber = 0;
            LoggInnKnapp.Visibility = Visibility.Hidden;
            LoggUtKnapp.Visibility = Visibility.Hidden;
            ColorImage.Visibility = Visibility.Visible;


            PhotoFrame.Visibility = Visibility.Hidden;
            AvbrytButton.Visibility = Visibility.Hidden;
            LogMeOut.Visibility = Visibility.Hidden;
            ForrigeButton.Visibility = Visibility.Hidden;
            NesteButton.Visibility = Visibility.Hidden;
        }
        private void NextPicture()
        {

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
                
            }
            InfoText.Text = "Bilde " + PictureNumber;
           
        }

        private void PreviousPicture()
        {
            
            //Preventing PictureNumber outofbound
            if (PictureNumber == 0)
            {
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
            InfoText.Text = "Bilde " + PictureNumber;
        }
        private void LoginClicked(object sender, RoutedEventArgs e)
        {
            PersonIn();
        }
  

        private void LogoutClicked(object sender, RoutedEventArgs e)
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
             CopyPicture(FilesInDirectory[PictureNumber]);
         
        }

        private void AvbrytClicked(object sender, RoutedEventArgs e)
        {
           ShowStartScreen();
            
        }
    }
}

