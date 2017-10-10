using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Media.Ocr;
using Windows.Media.SpeechSynthesis;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Assignment_2_PNUI
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// Brush for drawing the bounding box around each detected face.
        /// </summary>
        private readonly SolidColorBrush lineBrush = new SolidColorBrush(Windows.UI.Colors.Yellow);

        /// <summary>
        /// Thickness of the face bounding box lines.
        /// </summary>
        private readonly double lineThickness = 2.0;

        /// <summary>
        /// Transparent fill for the bounding box.
        /// </summary>
        private readonly SolidColorBrush fillBrush = new SolidColorBrush(Windows.UI.Colors.Transparent);

        /// <summary>
        /// Limit on the height of the source image (in pixels) passed into FaceDetector for performance considerations.
        /// Images larger that this size will be downscaled proportionally.
        /// </summary>
        /// <remarks>
        /// This is an arbitrary value that was chosen for this scenario, in which FaceDetector performance isn't too important but face
        /// detection accuracy is; a generous size is used.
        /// Your application may have different performance and accuracy needs and you'll need to decide how best to control input.
        /// </remarks>
        private readonly uint sourceImageHeightLimit = 1280;


        // Bitmap holder of currently loaded image.
        private SoftwareBitmap originalBitmap;

        // Bitmap source for Canvas
        private WriteableBitmap displaySource;

        // List of detected faces
        IList<DetectedFace> faces = null;

        // FaceDetector input image 
        SoftwareBitmap detectorInput = null;

        // Image brush to draw onto Canvas
        private ImageBrush brush;

        // Recognized words overlay boxes.
        private List<WordOverlay> wordBoxes = new List<WordOverlay>();

        private SpeechSynthesizer synthesizer;

        private FileOpenPicker picker;
        private StorageFile file;

        public MainPage()
        {
            this.InitializeComponent();
            this.synthesizer = new SpeechSynthesizer();
            this.UpdateAvailableLanguages();
        }

        /// <summary>
        /// This is event handler for 'Extract' button.
        /// Recognizes text from image and displays it.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ExtractButton_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            RecognizeOCR();            
        }

        private async void RecognizeOCR()
        {
            ClearResults();

            if (originalBitmap == null)
            {
                NotifyUser("Please open image file first!", NotifyType.ErrorMessage);
                return;
            }

            // Check if OcrEngine supports image resoulution.
            if (originalBitmap.PixelWidth > OcrEngine.MaxImageDimension || originalBitmap.PixelHeight > OcrEngine.MaxImageDimension)
            {
                this.NotifyUser(
                    String.Format("Bitmap dimensions ({0}x{1}) are too big for OCR.", originalBitmap.PixelWidth, originalBitmap.PixelHeight) +
                    "Max image dimension is " + OcrEngine.MaxImageDimension + ".",
                    NotifyType.ErrorMessage);

                return;
            }

            OcrEngine ocrEngine = null;

            if (UserLanguageToggle.IsOn)
            {
                // Try to create OcrEngine for first supported language from UserProfile.GlobalizationPreferences.Languages list.
                // If none of the languages are available on device, method returns null.
                ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
            else
            {
                // Try to create OcrEngine for specified language.
                // If language is not supported on device, method returns null.
                ocrEngine = OcrEngine.TryCreateFromLanguage(LanguageList.SelectedValue as Language);
            }

            if (ocrEngine != null)
            {
                // Recognize text from image.
                var ocrResult = await ocrEngine.RecognizeAsync(originalBitmap);

                // Display recognized text.
                ExtractedTextBox.Text = ocrResult.Text;

                if (ocrResult.TextAngle != null)
                {
                    // If text is detected under some angle in this sample scenario we want to
                    // overlay word boxes over original image, so we rotate overlay boxes.
                    TextOverlay.RenderTransform = new RotateTransform
                    {
                        Angle = (double)ocrResult.TextAngle,
                        CenterX = PhotoCanvas.ActualWidth / 2,
                        CenterY = PhotoCanvas.ActualHeight / 2
                    };
                }

                // Create overlay boxes over recognized words.
                foreach (var line in ocrResult.Lines)
                {
                    Rect lineRect = Rect.Empty;
                    foreach (var word in line.Words)
                    {
                        lineRect.Union(word.BoundingRect);
                    }

                    // Determine if line is horizontal or vertical.
                    // Vertical lines are supported only in Chinese Traditional and Japanese languages.
                    bool isVerticalLine = lineRect.Height > lineRect.Width;

                    foreach (var word in line.Words)
                    {
                        WordOverlay wordBoxOverlay = new WordOverlay(word);

                        // Keep references to word boxes.
                        wordBoxes.Add(wordBoxOverlay);

                        // Define overlay style.
                        var overlay = new Border()
                        {
                            Style = isVerticalLine ?
                                        (Style)Resources["HighlightedWordBoxVerticalLine"] :
                                        (Style)Resources["HighlightedWordBoxHorizontalLine"]
                        };

                        // Bind word boxes to UI.
                        overlay.SetBinding(MarginProperty, wordBoxOverlay.CreateWordPositionBinding());
                        overlay.SetBinding(WidthProperty, wordBoxOverlay.CreateWordWidthBinding());
                        overlay.SetBinding(HeightProperty, wordBoxOverlay.CreateWordHeightBinding());

                        // Put the filled textblock in the results grid.
                        TextOverlay.Children.Add(overlay);
                    }
                }

                // Rescale word boxes to match current UI size.
                UpdateWordBoxTransform();

                this.NotifyUser(
                    "Found " + ocrResult.Lines.Count + " lines of text, and " + wordBoxes.Count + " words.",
                    NotifyType.StatusMessage);

                Speak(ocrResult.Text);

            }
            else
            {
                this.NotifyUser("Selected language is not available.", NotifyType.ErrorMessage);
            }
        }

        private async void Speak(string text)
        {
            if (media.CurrentState.Equals(MediaElementState.Playing))
            {
                media.Stop();
            }
            else
            {
                if (!String.IsNullOrEmpty(text))
                {
                    try
                    {
                        SpeechSynthesisStream synthesisStream = await synthesizer.SynthesizeTextToStreamAsync(text);

                        // Set the source and start playing the synthesized 
                        // audio stream.
                        media.AutoPlay = true;
                        media.SetSource(synthesisStream, synthesisStream.ContentType);
                        media.Play();
                    }
                    catch (System.IO.FileNotFoundException)
                    {
                        // If media player components are unavailable, 
                        // (eg, using a N SKU of windows), we won't be able 
                        // to start media playback. Handle this gracefully 
                        var messageDialog = new Windows.UI.Popups.MessageDialog("Media player components unavailable");
                        await messageDialog.ShowAsync();
                    }
                    catch (Exception)
                    {
                        // If the text is unable to be synthesized, throw 
                        // an error message to the user.
                        media.AutoPlay = false;
                        var messageDialog = new Windows.UI.Popups.MessageDialog("Unable to synthesize text");
                        await messageDialog.ShowAsync();
                    }
                }
            }
        }

        /// <summary>
        /// This is event handler for "Detect Faces" button
        /// Detect faces from image and show to user
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DetectFacesButton_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (file == null)
            {
                NotifyUser("Please open image file first!", NotifyType.ErrorMessage);
                return;
            }

            DetectFaces();
        }

        private async void DetectFaces()
        {
            if (file != null)
            {
                // Open the image file and decode the bitmap into memory.
                // We'll need to make 2 bitmap copies: one for the FaceDetector and another to display.
                using (IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.Read))
                {
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(fileStream);
                    BitmapTransform transform = this.ComputeScalingTransformForSourceImage(decoder);

                    using (SoftwareBitmap originalBitmap = await decoder.GetSoftwareBitmapAsync(decoder.BitmapPixelFormat, BitmapAlphaMode.Ignore, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage))
                    {
                        // We need to convert the image into a format that's compatible with FaceDetector.
                        // Gray8 should be a good type but verify it against FaceDetector’s supported formats.
                        const BitmapPixelFormat InputPixelFormat = BitmapPixelFormat.Gray8;
                        if (FaceDetector.IsBitmapPixelFormatSupported(InputPixelFormat))
                        {
                            using (detectorInput = SoftwareBitmap.Convert(originalBitmap, InputPixelFormat))
                            {
                                // Create a WritableBitmap for our visualization display; copy the original bitmap pixels to wb's buffer.
                                displaySource = new WriteableBitmap(originalBitmap.PixelWidth, originalBitmap.PixelHeight);
                                originalBitmap.CopyToBuffer(displaySource.PixelBuffer);

                                NotifyUser("Detecting...", NotifyType.StatusMessage);

                                // Initialize our FaceDetector and execute it against our input image.
                                // NOTE: FaceDetector initialization can take a long time, and in most cases
                                // you should create a member variable and reuse the object.
                                // However, for simplicity in this scenario we instantiate a new instance each time.
                                FaceDetector detector = await FaceDetector.CreateAsync();
                                faces = await detector.DetectFacesAsync(detectorInput);

                                // Create our display using the available image and face results.
                                DrawDetectedFaces(displaySource, faces);
                            }
                        }
                        else
                        {
                            NotifyUser("PixelFormat '" + InputPixelFormat.ToString() + "' is not supported by FaceDetector", NotifyType.ErrorMessage);
                        }
                    }
                }
            }            
        }

        /// <summary>
        /// Takes the photo image and FaceDetector results and assembles the visualization onto the Canvas.
        /// </summary>
        /// <param name="displaySource">Bitmap object holding the image we're going to display</param>
        /// <param name="foundFaces">List of detected faces; output from FaceDetector</param>
        private void DrawDetectedFaces(WriteableBitmap displaySource, IList<DetectedFace> foundFaces)
        {        
            if (foundFaces != null)
            {
                double widthScale = displaySource.PixelWidth / this.PhotoCanvas.ActualWidth;
                double heightScale = displaySource.PixelHeight / this.PhotoCanvas.ActualHeight;

                foreach (DetectedFace face in foundFaces)
                {
                    // Create a rectangle element for displaying the face box but since we're using a Canvas
                    // we must scale the rectangles according to the image’s actual size.
                    // The original FaceBox values are saved in the Rectangle's Tag field so we can update the
                    // boxes when the Canvas is resized.
                    Rectangle box = new Rectangle();
                    box.Tag = face.FaceBox;
                    box.Width = (uint)(face.FaceBox.Width / widthScale);
                    box.Height = (uint)(face.FaceBox.Height / heightScale);
                    box.Fill = this.fillBrush;
                    box.Stroke = this.lineBrush;
                    box.StrokeThickness = this.lineThickness;
                    box.Margin = new Thickness((uint)(face.FaceBox.X / widthScale), (uint)(face.FaceBox.Y / heightScale), 0, 0);

                    PhotoCanvas.Children.Add(box);
                }
            }

            string message;
            if (foundFaces == null || foundFaces.Count == 0)
            {
                message = "Didn't find any human faces in the image";
            }
            else if (foundFaces.Count == 1)
            {
                message = "Found a human face in the image";
            }
            else
            {
                message = "Found " + foundFaces.Count + " human faces in the image";
            }

            NotifyUser(message, NotifyType.StatusMessage);
        }

        /// <summary>
        /// Computes a BitmapTransform to downscale the source image if it's too large. 
        /// </summary>
        /// <remarks>
        /// Performance of the FaceDetector degrades significantly with large images, and in most cases it's best to downscale
        /// the source bitmaps if they're too large before passing them into FaceDetector. Remember through, your application's performance needs will vary.
        /// </remarks>
        /// <param name="sourceDecoder">Source image decoder</param>
        /// <returns>A BitmapTransform object holding scaling values if source image is too large</returns>
        private BitmapTransform ComputeScalingTransformForSourceImage(BitmapDecoder sourceDecoder)
        {
            BitmapTransform transform = new BitmapTransform();

            if (sourceDecoder.PixelHeight > this.sourceImageHeightLimit)
            {
                float scalingFactor = (float)this.sourceImageHeightLimit / (float)sourceDecoder.PixelHeight;

                transform.ScaledWidth = (uint)Math.Floor(sourceDecoder.PixelWidth * scalingFactor);
                transform.ScaledHeight = (uint)Math.Floor(sourceDecoder.PixelHeight * scalingFactor);
            }

            return transform;
        }

        /// <summary>
        /// This is event handler for 'Load' button.
        /// It opens file picked and load selected image in UI..
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void LoadButton_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            picker = new FileOpenPicker()
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                FileTypeFilter = { ".jpg", ".jpeg", ".png" },
            };

            file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                ClearResults();

                await LoadImage(file);

                this.NotifyUser(
                    String.Format("Loaded image from file: {0} ({1}x{2}).", file.Name, originalBitmap.PixelWidth, originalBitmap.PixelHeight),
                    NotifyType.StatusMessage);
            }
        }

        /// <summary>
        /// Loads image from file to bitmap and displays it in UI.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        private async Task LoadImage(StorageFile file)
        {
            using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read))
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);

                originalBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                var imgSource = new WriteableBitmap(originalBitmap.PixelWidth, originalBitmap.PixelHeight);
                originalBitmap.CopyToBuffer(imgSource.PixelBuffer);
                //PreviewImage.Source = imgSource;

                brush = new ImageBrush { ImageSource = imgSource };
                PhotoCanvas.Background = brush;
                PhotoCanvas.Width = imgSource.PixelWidth;
                PhotoCanvas.Height = imgSource.PixelHeight;
            }
        }

        /// <summary>
        /// When canvas size changes transform recognized words as well
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CanvasSize_Changed(object sender, SizeChangedEventArgs e)
        {
            UpdateWordBoxTransform();

            // Update image rotation center.
            var rotate = TextOverlay.RenderTransform as RotateTransform;
            if (rotate != null)
            {
                rotate.CenterX = PhotoCanvas.ActualWidth / 2;
                rotate.CenterY = PhotoCanvas.ActualHeight / 2;
            }
        }

        private void UpdateWordBoxTransform()
        {
            if (originalBitmap != null)
            {
                // Used for text overlay.
                // Prepare scale transform for words since image is not displayed in original size.
                ScaleTransform scaleTrasform = new ScaleTransform
                {
                    CenterX = 0,
                    CenterY = 0,
                    ScaleX = PhotoCanvas.ActualWidth / originalBitmap.PixelWidth,
                    ScaleY = PhotoCanvas.ActualHeight / originalBitmap.PixelHeight
                };

                foreach (var item in wordBoxes)
                {
                    item.Transform(scaleTrasform);
                }
            }
            
        }

        private void LanguageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ClearResults();

            var lang = LanguageList.SelectedValue as Language;
            if (lang != null)
            {
                this.NotifyUser(
                    "Selected OCR language is " + lang.DisplayName + ". " +
                        OcrEngine.AvailableRecognizerLanguages.Count + " OCR language(s) are available. " +
                        "Check combo box for full list.",
                    NotifyType.StatusMessage);
            }
        }

        private void ClearResults()
        {
            TextOverlay.RenderTransform = null;
            ExtractedTextBox.Text = String.Empty;
            TextOverlay.Children.Clear();
            wordBoxes.Clear();
        }

        private void UserLanguageToggle_Toggled(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            UpdateAvailableLanguages();
        }

        private void UpdateAvailableLanguages()
        {
            if (!UserLanguageToggle.IsOn)
            {
                // Check if any OCR language is available on device.
                if (OcrEngine.AvailableRecognizerLanguages.Count > 0)
                {
                    LanguageList.ItemsSource = OcrEngine.AvailableRecognizerLanguages;
                    LanguageList.SelectedIndex = 0;
                    LanguageList.IsEnabled = true;
                }
                else
                {
                    // Prevent OCR if no OCR languages are available on device.
                    UserLanguageToggle.IsEnabled = false;
                    LanguageList.IsEnabled = false;
                    ExtractButton.IsEnabled = false;

                    this.NotifyUser("No available OCR languages.", NotifyType.ErrorMessage);
                }
            }
            else
            {
                LanguageList.ItemsSource = null;
                LanguageList.IsEnabled = false;

                NotifyUser(
                    "Run OCR in first OCR available language from UserProfile.GlobalizationPreferences.Languages list.",
                    NotifyType.StatusMessage);
            }
        }
        
        public void NotifyUser(string strMessage, NotifyType type)
        {
            // If called from the UI thread, then update immediately.
            // Otherwise, schedule a task on the UI thread to perform the update.
            if (Dispatcher.HasThreadAccess)
            {
                UpdateStatus(strMessage, type);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateStatus(strMessage, type));
            }
        }

        private void UpdateStatus(string strMessage, NotifyType type)
        {
            switch (type)
            {
                case NotifyType.StatusMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Green);
                    break;
                case NotifyType.ErrorMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Red);
                    break;
            }

            StatusBlock.Text = strMessage;

            // Collapse the StatusBlock if it has no text to conserve real estate.
            StatusBorder.Visibility = (StatusBlock.Text != String.Empty) ? Visibility.Visible : Visibility.Collapsed;
            if (StatusBlock.Text != String.Empty)
            {
                StatusBorder.Visibility = Visibility.Visible;
                StatusPanel.Visibility = Visibility.Visible;
            }
            else
            {
                StatusBorder.Visibility = Visibility.Collapsed;
                StatusPanel.Visibility = Visibility.Collapsed;
            }
        }

        public enum NotifyType
        {
            StatusMessage,
            ErrorMessage
        };
    }


}
