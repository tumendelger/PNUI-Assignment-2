using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Media.SpeechSynthesis;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Assignment_2_PNUI
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // Recognized words overlay boxes.
        private List<WordOverlay> wordBoxes = new List<WordOverlay>();

        // Bitmap holder of currently loaded image.
        private SoftwareBitmap bitmap;

        private SpeechSynthesizer synthesizer;

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
            this.RecognizeOCR();
        }

        private async void RecognizeOCR()
        {
            ClearResults();

            // Check if OcrEngine supports image resoulution.
            if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
            {
                this.NotifyUser(
                    String.Format("Bitmap dimensions ({0}x{1}) are too big for OCR.", bitmap.PixelWidth, bitmap.PixelHeight) +
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
                var ocrResult = await ocrEngine.RecognizeAsync(bitmap);

                // Display recognized text.
                ExtractedTextBox.Text = ocrResult.Text;

                if (ocrResult.TextAngle != null)
                {
                    // If text is detected under some angle in this sample scenario we want to
                    // overlay word boxes over original image, so we rotate overlay boxes.
                    TextOverlay.RenderTransform = new RotateTransform
                    {
                        Angle = (double)ocrResult.TextAngle,
                        CenterX = PreviewImage.ActualWidth / 2,
                        CenterY = PreviewImage.ActualHeight / 2
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
                                        (Style)this.Resources["HighlightedWordBoxVerticalLine"] :
                                        (Style)this.Resources["HighlightedWordBoxHorizontalLine"]
                        };

                        // Bind word boxes to UI.
                        overlay.SetBinding(Border.MarginProperty, wordBoxOverlay.CreateWordPositionBinding());
                        overlay.SetBinding(Border.WidthProperty, wordBoxOverlay.CreateWordWidthBinding());
                        overlay.SetBinding(Border.HeightProperty, wordBoxOverlay.CreateWordHeightBinding());

                        // Put the filled textblock in the results grid.
                        TextOverlay.Children.Add(overlay);
                    }
                }

                // Rescale word boxes to match current UI size.
                UpdateWordBoxTransform();

                this.NotifyUser(
                    "Image is OCRed for " + ocrEngine.RecognizerLanguage.DisplayName + " language.",
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

        private async void SampleButton_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ClearResults();

            // Load sample "Hello World" image.
            await LoadSampleImage();

            this.NotifyUser("Loaded sample image.", NotifyType.StatusMessage);
        }        

        /// <summary>
        /// This is event handler for 'Load' button.
        /// It opens file picked and load selected image in UI..
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void LoadButton_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var picker = new FileOpenPicker()
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                FileTypeFilter = { ".jpg", ".jpeg", ".png" },
            };

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                ClearResults();

                await LoadImage(file);

                this.NotifyUser(
                    String.Format("Loaded image from file: {0} ({1}x{2}).", file.Name, bitmap.PixelWidth, bitmap.PixelHeight),
                    NotifyType.StatusMessage);
            }
        }

        private async Task LoadSampleImage()
        {
            // Load sample "Hello World" image.
            var file = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFileAsync("Assets\\people-03.jpg");
            await LoadImage(file);
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

                bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                var imgSource = new WriteableBitmap(bitmap.PixelWidth, bitmap.PixelHeight);
                bitmap.CopyToBuffer(imgSource.PixelBuffer);
                PreviewImage.Source = imgSource;
            }
        }

        private void PreviewImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateWordBoxTransform();

            // Update image rotation center.
            var rotate = TextOverlay.RenderTransform as RotateTransform;
            if (rotate != null)
            {
                rotate.CenterX = PreviewImage.ActualWidth / 2;
                rotate.CenterY = PreviewImage.ActualHeight / 2;
            }
        }

        private void UpdateWordBoxTransform()
        {
            // Used for text overlay.
            // Prepare scale transform for words since image is not displayed in original size.
            ScaleTransform scaleTrasform = new ScaleTransform
            {
                CenterX = 0,
                CenterY = 0,
                ScaleX = PreviewImage.ActualWidth / bitmap.PixelWidth,
                ScaleY = PreviewImage.ActualHeight / bitmap.PixelHeight
            };

            foreach (var item in wordBoxes)
            {
                item.Transform(scaleTrasform);
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
