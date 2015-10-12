﻿using System;
using System.Text;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Notifications;
using Windows.UI.Xaml.Controls.Primitives;
using Microsoft.Maker.RemoteWiring;
using System.Diagnostics;
using Windows.UI.Xaml.Input;
using Windows.UI.Text;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace remote_wiring_experience
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        /*
         * we want to programatically create our UI so that we can eventually support any Arduino type
         * but for now we will define our values as constant member variables
         */
        private const int numberOfAnalogPins = 6;
        private const int numberOfDigitalPins = 14;
        private static byte[] pwmPins = { 3, 5, 6, 9, 10, 11, 13 };
        private int numberOfPwmPins = pwmPins.Length;
        private static byte[] i2cPins = { 18, 19 };
        private bool isI2cEnabled = false;

        //stores image assets so that they can be loaded once and reused many times
        private Dictionary<string, BitmapImage> bitmaps;

        //these dictionaries store the loaded UI elements for easy access by pin number
        private Dictionary<byte, ToggleSwitch> digitalModeToggleSwitches;
        private Dictionary<byte, ToggleSwitch> digitalStateToggleSwitches;
        private Dictionary<byte, TextBlock> digitalStateTextBlocks;
        private Dictionary<byte, ToggleSwitch> analogModeToggleSwitches;
        private Dictionary<byte, Slider> analogSliders;
        private Dictionary<byte, TextBlock> analogTextBlocks;
        private Dictionary<byte, ToggleSwitch> pwmModeToggleSwitches;
        private Dictionary<byte, TextBlock> pwmTextBlocks;
        private Dictionary<byte, Slider> pwmSliders;
        
        private RemoteDevice arduino;

        //telemetry-related items
        DateTime lastPivotNavigationTime;

        private int currentPage = 0;
        private String[] pages = { "Digital", "Analog", "PWM", "About" };
        private bool navigated = false;
        private bool resetVoltage = false;

        public MainPage()
        {
            this.InitializeComponent();

            bitmaps = new Dictionary<string, BitmapImage>();

            digitalModeToggleSwitches = new Dictionary<byte, ToggleSwitch>();
            digitalStateToggleSwitches = new Dictionary<byte, ToggleSwitch>();
            digitalStateTextBlocks = new Dictionary<byte, TextBlock>();
            analogModeToggleSwitches = new Dictionary<byte, ToggleSwitch>();
            analogSliders = new Dictionary<byte, Slider>();
            analogTextBlocks = new Dictionary<byte, TextBlock>();
            pwmModeToggleSwitches = new Dictionary<byte, ToggleSwitch>();
            pwmTextBlocks = new Dictionary<byte, TextBlock>();
            pwmSliders = new Dictionary<byte, Slider>();
        }

        protected override void OnNavigatedTo( NavigationEventArgs e )
        {
            base.OnNavigatedTo( e );
            //LoadAssets();
            LoadPinPages();
            arduino = App.Arduino;
            arduino.DigitalPinUpdated += Arduino_OnDigitalPinUpdated;
            arduino.AnalogPinUpdated += Arduino_OnAnalogPinUpdated;

            for (byte pin = 0; pin < numberOfDigitalPins; ++pin)
            {
                UpdateDigitalPinIndicators(pin);
            }

            App.Telemetry.TrackPageView("Digital_Controls_Page");
            lastPivotNavigationTime = DateTime.Now;
        }


        //******************************************************************************
        //* Windows Remote Arduino callbacks
        //******************************************************************************

        /// <summary>
        /// This function is called when the Windows Remote Arduino library reports that an input value has changed for an analog pin.
        /// </summary>
        /// <param name="pin">The pin whose value has changed</param>
        /// <param name="value">the new value of the pin</param>
        private void Arduino_OnAnalogPinUpdated( byte pin, ushort value )
        {
            //we must dispatch the change to the UI thread to update the text field.
            var action = Dispatcher.RunAsync( Windows.UI.Core.CoreDispatcherPriority.Normal, new Windows.UI.Core.DispatchedHandler( () =>
            {
                UpdateAnalogIndicators( pin, value );
                UpdatePwmPinModeIndicator(pin);
            } ) );
        }


        /// <summary>
        /// This function is called when the Windows Remote Arduino library reports that an input value has changed for a digital pin.
        /// </summary>
        /// <param name="pin">The pin whose value has changed</param>
        /// <param name="state">the new state of the pin, either HIGH or LOW</param>
        private void Arduino_OnDigitalPinUpdated( byte pin, PinState state )
        {
            //we must dispatch the change to the UI thread to change the indicator image
            var action = Dispatcher.RunAsync( Windows.UI.Core.CoreDispatcherPriority.Normal, new Windows.UI.Core.DispatchedHandler( () =>
            {
                UpdateDigitalPinIndicators( pin );
            } ) );
        }


        //******************************************************************************
        //* Button Callbacks
        //******************************************************************************


        /// <summary>
        /// Invoked when the analog mode toggle button is tapped or pressed
        /// </summary>
        /// <param name="sender">the button being pressed</param>
        /// <param name="args">button press event args</param>
        private void OnClick_DigitalModeToggleSwitch( object sender, RoutedEventArgs args )
        {
            // This bool fixes the bug where voltage returns to 0v after PWM but the slider still represents 5v.
            // Needed because switching from PWM to input to output automatically sets the pin to 0v.
            if (!navigated)
            {
                var button = sender as ToggleSwitch;
                var pin = GetPinFromButtonObject(button);

                //pins 0 and 1 are the serial pins and are in use. this manual check will show them as disabled
                if (pin == 0 || pin == 1)
                {
                    ShowToast("Pin unavailable.", "That pin is in use as a serial pin and cannot be used.", null);
                    return;
                }

                var mode = arduino.getPinMode(pin);
                var nextMode = (mode == PinMode.OUTPUT) ? PinMode.INPUT : PinMode.OUTPUT;

                // Fixes bug where voltage returns to 0v after pin input but slider still represents 5v.
                // Needed because switching to output mode automatically sets pin to 0v.
                resetVoltage = true;
                if (nextMode == PinMode.OUTPUT)
                {
                    digitalStateToggleSwitches[pin].IsOn = false;
                }
                resetVoltage = false;

                arduino.pinMode(pin, nextMode);

                //telemetry
                var properties = new Dictionary<string, string>();
                properties.Add("pin_number", pin.ToString());
                properties.Add("new_mode", nextMode.ToString());
                App.Telemetry.TrackEvent("Digital_Mode_Toggle_Button_Pressed", properties);

                UpdateDigitalPinIndicators(pin);
            }
        }

        /// <summary>
        /// Invoked when the digital state toggle button is tapped or pressed
        /// </summary>
        /// <param name="sender">the button being pressed</param>
        /// <param name="args">button press event args</param>
        private void OnClick_DigitalStateToggleSwitch( object sender, RoutedEventArgs args )
        {
            if (!resetVoltage)
            {
                var button = sender as ToggleSwitch;
                var pin = GetPinFromButtonObject(button);

                //pins 0 and 1 are the serial pins and are in use. this manual check will show them as disabled
                if (pin == 0 || pin == 1)
                {
                    ShowToast("Pin unavailable.", "That pin is in use as a serial pin and cannot be used.", null);
                    return;
                }

                if (arduino.getPinMode(pin) != PinMode.OUTPUT)
                {
                    ShowToast("Incorrect PinMode!", "You must first set this pin to OUTPUT.", null);
                    return;
                }

                var state = arduino.digitalRead(pin);
                var nextState = (state == PinState.HIGH) ? PinState.LOW : PinState.HIGH;

                arduino.digitalWrite(pin, nextState);

                //telemetry
                var properties = new Dictionary<string, string>();
                properties.Add("pin_number", pin.ToString());
                properties.Add("new_state", nextState.ToString());
                App.Telemetry.TrackEvent("Digital_State_Toggle_Button_Pressed", properties);

                UpdateDigitalPinIndicators(pin);
            }
        }


        /// <summary>
        /// Invoked when the analog mode toggle button is tapped or pressed
        /// </summary>
        /// <param name="sender">the button being pressed</param>
        /// <param name="args">button press event args</param>
        private void OnClick_AnalogModeToggleSwitch(object sender, RoutedEventArgs args)
        {
            var button = sender as ToggleSwitch;
            var pin = GetPinFromButtonObject(button);
            var analogPinNumber = ConvertAnalogPinToPinNumber(pin);

            //var mode = arduino.getPinMode(analogPinNumber);
            var mode = arduino.getPinMode("A" + pin);
            var nextMode = (mode == PinMode.OUTPUT) ? PinMode.ANALOG : PinMode.OUTPUT;

            arduino.pinMode("A" + pin, nextMode);

            //telemetry
            var properties = new Dictionary<string, string>();
            properties.Add("pin_number", pin.ToString());
            properties.Add("new_mode", nextMode.ToString());
            App.Telemetry.TrackEvent("Analog_Mode_Toggle_Button_Pressed", properties);

            UpdateAnalogPinModeIndicator(pin);
        }

        /// <summary>
        /// Invoked when the slider value for a PWM pin is modified.
        /// </summary>
        /// <param name="sender">the slider being manipulated</param>
        /// <param name="args">slider value changed event args</param>
        private void OnValueChanged_AnalogSlider(object sender, RangeBaseValueChangedEventArgs args)
        {
            var slider = sender as Slider;
            var pin = Convert.ToByte(slider.Name.Substring(slider.Name.IndexOf('_') + 1));

            arduino.analogWrite(pin, (byte)args.NewValue);
        }

        /// <summary>
        /// This function helps to process telemetry events when manipulation of a PWM slider is complete, 
        /// rather than after each tick.
        /// </summary>
        /// <param name="sender">the slider which was released</param>
        /// <param name="args">the slider release event args</param>
        private void OnPointerReleased_AnalogSlider(object sender, PointerRoutedEventArgs args)
        {
            var slider = sender as Slider;
            var pin = Convert.ToByte(slider.Name.Substring(slider.Name.IndexOf('_') + 1));

            //telemetry
            SendPwmTelemetryEvent(pin, slider.Value);
        }

        /// <summary>
        /// Invoked when the pwm mode toggle button is tapped or pressed
        /// </summary>
        /// <param name="sender">the button being pressed</param>
        /// <param name="args">button press event args</param>
        private void OnClick_PwmModeToggleSwitch(object sender, RoutedEventArgs args)
        {
            var button = sender as ToggleSwitch;
            var pin = GetPinFromButtonObject(button);

            var mode = arduino.getPinMode(pin);
            var nextMode = (mode == PinMode.PWM) ? PinMode.INPUT : PinMode.PWM;

            //telemetry
            var properties = new Dictionary<string, string>();
            properties.Add("pin_number", pin.ToString());
            properties.Add("new_state", nextMode.ToString());
            App.Telemetry.TrackEvent("Pwm_Mode_Toggle_Button_Pressed", properties);

            arduino.pinMode(pin, nextMode);
            UpdatePwmPinModeIndicator(pin);
        }

        /// <summary>
        /// Invoked when the slider value for a PWM pin is modified.
        /// </summary>
        /// <param name="sender">the slider being manipulated</param>
        /// <param name="args">slider value changed event args</param>
        private void OnValueChanged_PwmSlider( object sender, RangeBaseValueChangedEventArgs args )
        {
            var slider = sender as Slider;
            var pin = Convert.ToByte( slider.Name.Substring( slider.Name.IndexOf( '_' ) + 1 ) );

            //pwmTextBlocks[pin].Text = args.NewValue.ToString();
            arduino.analogWrite( pin, (byte)args.NewValue );
        }

        /// <summary>
        /// This function helps to process telemetry events when manipulation of a PWM slider is complete, 
        /// rather than after each tick.
        /// </summary>
        /// <param name="sender">the slider which was released</param>
        /// <param name="args">the slider release event args</param>
        private void OnPointerReleased_PwmSlider( object sender, PointerRoutedEventArgs args )
        {
            var slider = sender as Slider;
            var pin = Convert.ToByte( slider.Name.Substring( slider.Name.IndexOf( '_' ) + 1 ) );

            //telemetry
            SendPwmTelemetryEvent( pin, slider.Value );
        }

        /*/// <summary>
        /// Invoked when the text value for a PWM pin is modified
        /// </summary>
        /// <param name="sender">the slider being manipulated</param>
        /// <param name="args">slider value changed event args</param>
        private void OnTextChanged_PwmTextBox( object sender, TextChangedEventArgs e )
        {
            var textbox = sender as TextBox;
            var pin = Convert.ToByte( textbox.Name.Substring( textbox.Name.IndexOf( '_' ) + 1 ) );

            try
            {
                var newValue = Convert.ToInt32( textbox.Text );
                if( newValue < byte.MinValue || newValue > byte.MaxValue ) throw new FormatException();
                pwmSliders[pin].Value = newValue;
                textbox.BorderBrush = new SolidColorBrush( Windows.UI.Color.FromArgb( 0, 0, 0, 0 ) );
            }
            catch( FormatException )
            {
                textbox.BorderBrush = new SolidColorBrush( Windows.UI.Color.FromArgb( 255, 255, 0, 0 ) );
            }
        }*/

        /*/// <summary>
        /// This function helps to process telemetry events when manipulation of a PWM text box is complete, 
        /// rather than after each character is typed
        /// </summary>
        /// <param name="sender">the text box which was manipulated</param>
        /// <param name="e">the lost focus event args</param>
        private void OnLostFocus_PwmTextBox( object sender, RoutedEventArgs e )
        {
            var slider = sender as Slider;
            var pin = Convert.ToByte( slider.Name.Substring( slider.Name.IndexOf( '_' ) + 1 ) );

            //telemetry
            SendPwmTelemetryEvent( pin, slider.Value );
        }*/


        //******************************************************************************
        //* UI Support Functions
        //******************************************************************************

        /// <summary>
        /// This function loads all of the necessary bitmaps that will be used by this program into the resource dictionary
        /// </summary>
        /*private void LoadAssets()
        {
            bitmaps.Add( "high", new BitmapImage( new Uri( BaseUri, @"Assets/high.png" ) ) );
            bitmaps.Add( "low", new BitmapImage( new Uri( BaseUri, @"Assets/low.png" ) ) );
            bitmaps.Add( "analog", new BitmapImage( new Uri( BaseUri, @"Assets/analog.png" ) ) );
            bitmaps.Add( "enabled", new BitmapImage( new Uri( BaseUri, @"Assets/enabled.png" ) ) );
            bitmaps.Add( "disabled", new BitmapImage( new Uri( BaseUri, @"Assets/disabled.png" ) ) );
            bitmaps.Add( "enablei2c", new BitmapImage( new Uri( BaseUri, @"Assets/enablei2c.png" ) ) );
            bitmaps.Add( "inuse_0", new BitmapImage( new Uri( BaseUri, @"Assets/inuse_0.png" ) ) );
            bitmaps.Add( "inuse_1", new BitmapImage( new Uri( BaseUri, @"Assets/inuse_1.png" ) ) );

            for( int i = 0; i < numberOfAnalogPins; ++i )
            {
                bitmaps.Add( "none_a" + i, new BitmapImage( new Uri( BaseUri, @"Assets/none_a" + i + ".png" ) ) );
                bitmaps.Add( "disabled_a" + i, new BitmapImage( new Uri( BaseUri, @"Assets/disabled_a" + i + ".png" ) ) );
                bitmaps.Add( "input_a" + i, new BitmapImage( new Uri( BaseUri, @"Assets/input_a" + i + ".png" ) ) );
            }

            for( int i = 0; i < numberOfDigitalPins; ++i )
            {
                bitmaps.Add( "output_" + i, new BitmapImage( new Uri( BaseUri, @"Assets/output_" + i + ".png" ) ) );
                bitmaps.Add( "disabled_" + i, new BitmapImage( new Uri( BaseUri, @"Assets/disabled_" + i + ".png" ) ) );
                bitmaps.Add( "input_" + i, new BitmapImage( new Uri( BaseUri, @"Assets/input_" + i + ".png" ) ) );
            }

            for( int i = 0; i < numberOfPwmPins; ++i )
            {
                bitmaps.Add( "pwm_" + pwmPins[i], new BitmapImage( new Uri( BaseUri, @"Assets/pwm_" + pwmPins[i] + ".png" ) ) );
            }
        }*/


        /// <summary>
        /// This function is called when a page is loaded either by swipe navigation or clicking the tabs at the top
        /// </summary>
        /// <param name="sender">The pivot which is loading the item</param>
        /// <param name="args">relative arguments, including the item that is being loaded</param>
        /*private void Pivot_PivotItemLoaded( Pivot sender, PivotItemEventArgs args )
        {
            lastPivotNavigationTime = DateTime.Now;
            switch( args.Item.Name )
            {
                case "Digital":
                    App.Telemetry.TrackPageView( "Digital_Controls_Page" );
                    UpdateDigitalControls();
                    break;

                case "Analog":
                    App.Telemetry.TrackPageView( "Analog_Controls_Page" );
                    UpdateAnalogControls();
                    break;
            }
            uiControlsLoaded[args.Item.Name] = true;
        }*/

        /// <summary>
        /// This function loads the content of each of the pin pages, as well as the About page.  The reason this is done dynamically here, instead of statically in the XAML, is to leave the code open
        /// to the possibility of dynamically filling the pages based on the specific pin numbers/orientations of the connected board.
        /// </summary>
        private void LoadPinPages()
        {
            // Load the Digital page content.
            loadDigitalControls();
            loadAnalogControls();
            loadPWMControls();
        }

        /// <summary>
        /// This function is called when a pivot page is unloading either by swipe navigation to another page or clicking another tab at the top
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /*private void Pivot_PivotItemUnloading( Pivot sender, PivotItemEventArgs args )
        {
            App.Telemetry.TrackMetric( "Pivot_" + sender.Name + "_Time_Spent_In_Seconds", ( DateTime.Now - lastPivotNavigationTime ).TotalSeconds );
        }*/

        /*/// <summary>
        /// Updates the UI for the analog control page as necessary
        /// </summary>
        private void UpdateAnalogControls()
        {
            //if( !uiControlsLoaded["Analog"] ) loadAnalogControls();
            for( byte pin = 0; pin < numberOfAnalogPins; ++pin )
            {
                UpdateAnalogPinModeIndicator( pin );
            }

            for( byte i = 0; i < numberOfPwmPins; ++i )
            {
                UpdatePwmPinModeIndicator( pwmPins[i] );
            }
        }*/

        /*/// <summary>
        /// Updates the UI for the digital control page as necessary
        /// </summary>
        private void UpdateDigitalControls()
        {
            //if( !uiControlsLoaded["Digital"] ) loadDigitalControls();
            for( byte pin = 0; pin < numberOfDigitalPins; ++pin )
            {
                UpdateDigitalPinIndicators( pin );
            }
        }*/

        /// <summary>
        /// Adds the necessary digital controls to a StackPanel created for the Digital page.  This will only be called on navigation from the Connections page.
        /// </summary>
        private void loadDigitalControls()
        {
            //add controls and state change indicators/buttons for each digital pin the board supports
            for (byte i = 0; i < numberOfDigitalPins; ++i)
            {
                // Container stack to hold all pieces of new row of pins.
                var containerStack = new StackPanel();
                containerStack.Orientation = Orientation.Horizontal;
                containerStack.FlowDirection = FlowDirection.LeftToRight;
                containerStack.HorizontalAlignment = HorizontalAlignment.Stretch;
                containerStack.Margin = new Thickness(8, 0, 0, 20);

                // Set up the pin text.
                var textStack = new StackPanel();
                textStack.Orientation = Orientation.Vertical;
                textStack.FlowDirection = FlowDirection.LeftToRight;
                textStack.HorizontalAlignment = HorizontalAlignment.Stretch;

                var text = new TextBlock();
                text.HorizontalAlignment = HorizontalAlignment.Stretch;
                text.VerticalAlignment = VerticalAlignment.Center;
                text.Margin = new Thickness(0, 0, 0, 0);
                text.Text = "Pin " + i;
                text.FontSize = 14;
                text.FontWeight = FontWeights.SemiBold;

                var text2 = new TextBlock();
                text2.HorizontalAlignment = HorizontalAlignment.Stretch;
                text2.VerticalAlignment = VerticalAlignment.Center;
                text2.Margin = new Thickness(0, 0, 0, 0);
                text2.Text = "Digital";
                text2.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 107, 106));
                text2.FontSize = 14;
                text2.FontWeight = FontWeights.SemiBold;

                textStack.Children.Add(text);
                textStack.Children.Add(text2);
                containerStack.Children.Add(textStack);

                // Set up the mode toggle button.
                var modeStack = new StackPanel();
                modeStack.Orientation = Orientation.Horizontal;
                modeStack.FlowDirection = FlowDirection.LeftToRight;
                modeStack.HorizontalAlignment = HorizontalAlignment.Stretch;
                modeStack.Margin = new Thickness(92, 0, 0, 0);

                var toggleSwitch = new ToggleSwitch();
                toggleSwitch.HorizontalAlignment = HorizontalAlignment.Left;
                toggleSwitch.VerticalAlignment = VerticalAlignment.Center;
                toggleSwitch.Margin = new Thickness(5, 0, 5, 0);
                toggleSwitch.Name = "digitalmode_" + i;
                toggleSwitch.Toggled += OnClick_DigitalModeToggleSwitch;
                if (i == 1 || i == 0) { toggleSwitch.IsEnabled = false; }

                var onContent = new TextBlock();
                onContent.Text = "Input";
                onContent.FontSize = 14;
                toggleSwitch.OnContent = onContent;
                var offContent = new TextBlock();
                offContent.Text = "Output";
                if (i == 1 || i == 0) { offContent.Text = "Disabled"; }
                offContent.FontSize = 14;
                toggleSwitch.OffContent = offContent;
                digitalModeToggleSwitches.Add(i, toggleSwitch);

                modeStack.Children.Add(toggleSwitch);
                containerStack.Children.Add(modeStack);

                // Set up the state toggle button.
                var stateStack = new StackPanel();
                stateStack.Orientation = Orientation.Horizontal;
                stateStack.FlowDirection = FlowDirection.LeftToRight;
                stateStack.HorizontalAlignment = HorizontalAlignment.Stretch;

                var toggleSwitch2 = new ToggleSwitch();
                toggleSwitch2.HorizontalAlignment = HorizontalAlignment.Left;
                toggleSwitch2.VerticalAlignment = VerticalAlignment.Center;
                toggleSwitch2.Margin = new Thickness(1, 0, 5, 0);
                toggleSwitch2.Name = "digitalstate_" + i;
                toggleSwitch2.Toggled += OnClick_DigitalStateToggleSwitch;
                if (i == 1 || i == 0) { toggleSwitch2.IsEnabled = false; }

                var onContent2 = new TextBlock();
                onContent2.Text = "5v";
                onContent2.FontSize = 14;
                toggleSwitch2.OnContent = onContent2;
                var offContent2 = new TextBlock();
                offContent2.Text = "0v";
                if (i == 1 || i == 0) { offContent2.Text = "Disabled"; }
                offContent2.FontSize = 14;
                toggleSwitch2.OffContent = offContent2;
                digitalStateToggleSwitches.Add(i, toggleSwitch2);

                var text3 = new TextBlock();
                text3.HorizontalAlignment = HorizontalAlignment.Stretch;
                text3.VerticalAlignment = VerticalAlignment.Center;
                text3.Margin = new Thickness(0, 0, 0, 0);
                if (i == 1 || i == 0)
                {
                    text3.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 107, 106));
                    text3.Text = "Disabled for serial connection.";
                }
                else
                {
                    text3.Text = "0v";
                }
                text3.FontSize = 14;
                text3.Visibility = Visibility.Collapsed;
                digitalStateTextBlocks.Add(i, text3);

                stateStack.Children.Add(text3);
                stateStack.Children.Add(toggleSwitch2);
                containerStack.Children.Add(stateStack);

                // Add entire row to page.
                DigitalPins.Children.Add(containerStack);
            }
        }

        /// <summary>
        /// Adds the necessary analog controls to a StackPanel created for the Analog page. This will only be called on navigation from the Connections page.
        /// </summary>
        private void loadAnalogControls()
        {
            //add controls and text fields for each analog pin the board supports
            for( byte i = 0; i < numberOfAnalogPins; ++i )
            {
                // Container stack to hold all pieces of new row of pins.
                var containerStack = new StackPanel();
                containerStack.Orientation = Orientation.Horizontal;
                containerStack.FlowDirection = FlowDirection.LeftToRight;
                containerStack.HorizontalAlignment = HorizontalAlignment.Stretch;
                containerStack.Margin = new Thickness(8, 0, 0, 20);

                // Set up the pin text.
                var textStack = new StackPanel();
                textStack.Orientation = Orientation.Vertical;
                textStack.FlowDirection = FlowDirection.LeftToRight;
                textStack.HorizontalAlignment = HorizontalAlignment.Stretch;

                var text = new TextBlock();
                text.HorizontalAlignment = HorizontalAlignment.Stretch;
                text.VerticalAlignment = VerticalAlignment.Center;
                text.Margin = new Thickness(0, 0, 0, 0);
                text.Text = "Pin A" + i;
                text.FontSize = 14;
                text.FontWeight = FontWeights.SemiBold;

                var text2 = new TextBlock();
                text2.HorizontalAlignment = HorizontalAlignment.Stretch;
                text2.VerticalAlignment = VerticalAlignment.Center;
                text2.Margin = new Thickness(0, 0, 0, 0);
                text2.Text = "Analog";
                text2.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 107, 106));
                text2.FontSize = 14;
                text2.FontWeight = FontWeights.SemiBold;

                textStack.Children.Add(text);
                textStack.Children.Add(text2);
                containerStack.Children.Add(textStack);

                // Set up the mode toggle button.
                var modeStack = new StackPanel();
                modeStack.Orientation = Orientation.Horizontal;
                modeStack.FlowDirection = FlowDirection.LeftToRight;
                modeStack.HorizontalAlignment = HorizontalAlignment.Stretch;
                modeStack.Margin = new Thickness(88, 0, 0, 0);

                var toggleSwitch = new ToggleSwitch();
                toggleSwitch.HorizontalAlignment = HorizontalAlignment.Left;
                toggleSwitch.VerticalAlignment = VerticalAlignment.Center;
                toggleSwitch.Margin = new Thickness(5, 0, 5, 0);
                toggleSwitch.Name = "analogmode_" + i;
                toggleSwitch.Toggled += OnClick_AnalogModeToggleSwitch;

                var onContent = new TextBlock();
                onContent.Text = "Input";
                onContent.FontSize = 14;
                toggleSwitch.OnContent = onContent;
                var offContent = new TextBlock();
                offContent.Text = "Output";
                offContent.FontSize = 14;
                toggleSwitch.OffContent = offContent;
                analogModeToggleSwitches.Add(i, toggleSwitch);

                modeStack.Children.Add(toggleSwitch);
                containerStack.Children.Add(modeStack);

                /*//set up the value change slider
                var slider = new Slider();
                slider.Orientation = Orientation.Horizontal;
                slider.HorizontalAlignment = HorizontalAlignment.Stretch;
                slider.VerticalAlignment = VerticalAlignment.Center;
                slider.IsEnabled = true;
                slider.TickFrequency = 128;
                slider.SmallChange = 128;
                slider.Minimum = 0;
                slider.Maximum = 1023;
                slider.Name = "slider_" + i;
                slider.Width = 180;
                slider.Height = 34;
                slider.Margin = new Thickness(3, 0, 0, 0);
                slider.ValueChanged += OnValueChanged_AnalogSlider;
                slider.PointerReleased += OnPointerReleased_AnalogSlider;
                analogSliders.Add( i, slider );
                containerStack.Children.Add( slider );*/

                //set up the indication text
                var text3 = new TextBlock();
                text3.HorizontalAlignment = HorizontalAlignment.Stretch;
                text3.VerticalAlignment = VerticalAlignment.Center;
                text3.Margin = new Thickness( 2, 0, 0, 0 );
                text3.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 107, 106));
                text3.Text = "Cannot write to analog pins.";
                text3.FontSize = 14;
                analogTextBlocks.Add( i, text3 );
                containerStack.Children.Add( text3 );

                AnalogPins.Children.Add( containerStack );
            }
        }


        /// <summary>
        /// Adds the necessary analog controls to a StackPanel created for the PWM page. This will only be called on navigation from the Connections page.
        /// </summary>
        private void loadPWMControls()
        {
            //add controls and value sliders for each pwm pin the board supports
            for (byte i = 0; i < numberOfPwmPins; ++i)
            {
                // Container stack to hold all pieces of new row of pins.
                var containerStack = new StackPanel();
                containerStack.Orientation = Orientation.Horizontal;
                containerStack.FlowDirection = FlowDirection.LeftToRight;
                containerStack.HorizontalAlignment = HorizontalAlignment.Stretch;
                containerStack.Margin = new Thickness(8, 0, 0, 20);

                // Set up the pin text.
                var textStack = new StackPanel();
                textStack.Orientation = Orientation.Vertical;
                textStack.FlowDirection = FlowDirection.LeftToRight;
                textStack.HorizontalAlignment = HorizontalAlignment.Stretch;

                var text = new TextBlock();
                text.HorizontalAlignment = HorizontalAlignment.Stretch;
                text.VerticalAlignment = VerticalAlignment.Center;
                text.Margin = new Thickness(0, 0, 0, 0);
                text.Text = "Pin " + pwmPins[i];
                text.FontSize = 14;
                text.FontWeight = FontWeights.SemiBold;

                var text2 = new TextBlock();
                text2.HorizontalAlignment = HorizontalAlignment.Stretch;
                text2.VerticalAlignment = VerticalAlignment.Center;
                text2.Margin = new Thickness(0, 0, 0, 0);
                text2.Text = "PWM";
                text2.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 107, 106));
                text2.FontSize = 14;
                text2.FontWeight = FontWeights.SemiBold;

                textStack.Children.Add(text);
                textStack.Children.Add(text2);
                containerStack.Children.Add(textStack);

                // Set up the mode toggle button.
                var modeStack = new StackPanel();
                modeStack.Orientation = Orientation.Horizontal;
                modeStack.FlowDirection = FlowDirection.LeftToRight;
                modeStack.HorizontalAlignment = HorizontalAlignment.Stretch;
                modeStack.Margin = new Thickness(88, 0, 0, 0);

                var toggleSwitch = new ToggleSwitch();
                toggleSwitch.HorizontalAlignment = HorizontalAlignment.Left;
                toggleSwitch.VerticalAlignment = VerticalAlignment.Center;
                if (pwmPins[i] == 10 || pwmPins[i] == 13) { toggleSwitch.Margin = new Thickness(13, 0, 5, 0); }
                else { toggleSwitch.Margin = new Thickness(15, 0, 5, 0); }
                toggleSwitch.Name = "pwmmode_" + pwmPins[i];
                toggleSwitch.Toggled += OnClick_PwmModeToggleSwitch;

                var onContent = new TextBlock();
                onContent.Text = "Enabled";
                onContent.FontSize = 14;
                toggleSwitch.OnContent = onContent;
                var offContent = new TextBlock();
                offContent.Text = "Disabled";
                offContent.FontSize = 14;
                toggleSwitch.OffContent = offContent;
                pwmModeToggleSwitches.Add(pwmPins[i], toggleSwitch);

                modeStack.Children.Add(toggleSwitch);
                containerStack.Children.Add(modeStack);

                //set up the value change slider
                var slider = new Slider();
                slider.Visibility = Visibility.Collapsed;
                slider.Orientation = Orientation.Horizontal;
                slider.HorizontalAlignment = HorizontalAlignment.Stretch;
                slider.SmallChange = 32;
                slider.StepFrequency = 32;
                slider.TickFrequency = 32;
                slider.ValueChanged += OnValueChanged_PwmSlider;
                slider.PointerReleased += OnPointerReleased_PwmSlider;
                slider.Minimum = 0;
                slider.Maximum = 255;
                slider.Name = "pwmslider_" + pwmPins[i];
                slider.Width = 180;
                slider.Height = 34;
                slider.Margin = new Thickness(3, 0, 0, 0);
                pwmSliders.Add(pwmPins[i], slider);
                containerStack.Children.Add(slider);

                //set up the indication text
                var text3 = new TextBlock();
                text3.HorizontalAlignment = HorizontalAlignment.Stretch;
                text3.VerticalAlignment = VerticalAlignment.Center;
                text3.Margin = new Thickness(3, 0, 0, 0);
                text3.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 107, 106));
                text3.Text = "Enable PWM to write values.";
                text3.FontSize = 14;
                text3.Name = "pwmtext_" + pwmPins[i];
                text3.Visibility = Visibility.Visible;
                //text3.TextChanged += OnTextChanged_PwmTextBox;
                //text3.LostFocus += OnLostFocus_PwmTextBox;
                pwmTextBlocks.Add(pwmPins[i], text3);
                containerStack.Children.Add(text3);

                PWMPins.Children.Add(containerStack);
            }
        }
        
        /// <summary>
        /// Adds the necessary i2c controls to the i2c pivot page, this will only be called the first time this pivot page is loaded
        /// </summary>
        private void loadI2cControls()
        {
            var stack = new StackPanel();
            stack.Orientation = Orientation.Horizontal;
            stack.FlowDirection = FlowDirection.LeftToRight;
        }

        /// <summary>
        /// This function will determine which pin mode image should be applied for a given digital pin and apply it to the correct Image object
        /// </summary>
        /// <param name="pin">the pin number to be updated</param>
        private void UpdateDigitalPinIndicators(byte pin)
        {
            if (!digitalModeToggleSwitches.ContainsKey(pin)) return;

            //pins 0 and 1 are the serial pins and are in use. this manual check will show them as disabled
            if (pin == 0 || pin == 1)
            {
                digitalModeToggleSwitches[pin].IsEnabled = false;
                digitalStateToggleSwitches[pin].IsEnabled = false;
                digitalStateToggleSwitches[pin].Visibility = Visibility.Collapsed;
                digitalStateTextBlocks[pin].Visibility = Visibility.Visible;
            }
            else
                switch (arduino.getPinMode(pin))
                {
                    case PinMode.INPUT:
                        digitalModeToggleSwitches[pin].IsEnabled = true;
                        navigated = true;
                        digitalModeToggleSwitches[pin].IsOn = true;
                        navigated = false;
                        digitalStateToggleSwitches[pin].IsEnabled = true;
                        digitalStateToggleSwitches[pin].Visibility = Visibility.Collapsed;
                        digitalStateTextBlocks[pin].Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                        digitalStateTextBlocks[pin].Text = ((arduino.digitalRead(pin)) == PinState.HIGH) ? "5v" : "0v";
                        digitalStateTextBlocks[pin].Visibility = Visibility.Visible;
                        break;

                    case PinMode.OUTPUT:
                        digitalModeToggleSwitches[pin].IsEnabled = true;
                        digitalStateToggleSwitches[pin].IsEnabled = true;
                        digitalStateToggleSwitches[pin].Visibility = Visibility.Visible;
                        digitalStateTextBlocks[pin].Visibility = Visibility.Collapsed;
                        break;

                    default:
                    case PinMode.PWM:
                        digitalModeToggleSwitches[pin].IsEnabled = false;
                        digitalStateToggleSwitches[pin].IsEnabled = false;
                        digitalStateToggleSwitches[pin].Visibility = Visibility.Collapsed;
                        digitalStateTextBlocks[pin].Text = "Disabled for PWM use.";
                        digitalStateTextBlocks[pin].Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 107, 106));
                        digitalStateTextBlocks[pin].Visibility = Visibility.Visible;
                        break;
                }
        }

        /*/// <summary>
        /// This function will determine which indicator image should be applied for a given digital pin and apply it to the correct Image object
        /// </summary>
        /// <param name="pin">the pin number to be updated</param>
        private void UpdateDigitalPinStateIndicator( byte pin )
        {
            if( !digitalStateToggleSwitches.ContainsKey( pin ) ) return;

            //pins 0 and 1 are the serial pins and are in use. this manual check will show them as disabled
            if( pin == 0 || pin == 1 )
            {
                digitalStateToggleSwitches[pin].IsEnabled = false;
            }
            else
            {
                if (arduino.getPinMode(pin) == PinMode.PWM) digitalStateToggleSwitches[pin].IsEnabled = false;
                else if (arduino.digitalRead(pin) == PinState.HIGH) digitalStateToggleSwitches[pin].IsOn = true;
                else digitalStateToggleSwitches[pin].IsOn = false;
            }
        }*/

        /// <summary>
        /// This function will determine which pin mode image should be applied for a given analog pin and apply it to the correct Image object
        /// </summary>
        /// <param name="pin">the pin number to be updated</param>
        private void UpdateAnalogPinModeIndicator( byte pin )
        {
            if( !analogModeToggleSwitches.ContainsKey( pin ) ) return;

            var analogPinNumber = ConvertAnalogPinToPinNumber( pin );

            if( isI2cEnabled && ( analogPinNumber == i2cPins[0] || analogPinNumber == i2cPins[1] ) )
            {
                //analogSliders[pin].IsEnabled = false;
            }
            else
                switch( arduino.getPinMode( "A" + pin ) )
                {
                    case PinMode.ANALOG:
                        //analogSliders[pin].Visibility = Visibility.Collapsed;
                        analogTextBlocks[pin].Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                        analogTextBlocks[pin].Text = "" + arduino.analogRead("A" + pin);
                        break;

                    case PinMode.I2C:
                        //analogSliders[pin].Visibility = Visibility.Collapsed;
                        break;

                    default:
                        //analogSliders[pin].Visibility = Visibility.Visible;
                        analogTextBlocks[pin].Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 107, 106));
                        analogTextBlocks[pin].Text = "Cannot write to analog pins.";
                        break;
                }

        }

        /// <summary>
        /// This function will apply the given value to the given analog pin input
        /// </summary>
        /// <param name="pin">the pin number to be updated</param>
        /// <param name="value">the value to display</param>
        private void UpdateAnalogIndicators( byte pin, ushort value )
        {
            if( arduino.getPinMode( "A" + pin ) != PinMode.ANALOG ) return;
            if( analogTextBlocks.ContainsKey( pin ) ) analogTextBlocks[pin].Text = Convert.ToString( value );
            //if( analogSliders.ContainsKey( pin ) ) analogSliders[pin].Value = value;
        }

        /// <summary>
        /// This function will determine which pin mode image should be applied for a given pwm pin and apply it to the correct Image object
        /// </summary>
        /// <param name="pin">the pin number to be updated</param>
        private void UpdatePwmPinModeIndicator( byte pin )
        {
            if( !pwmModeToggleSwitches.ContainsKey( pin ) ) return;

            switch( arduino.getPinMode( pin ) )
            {
                case PinMode.PWM:
                    pwmSliders[pin].Visibility = Visibility.Visible;
                    pwmTextBlocks[pin].Visibility = Visibility.Collapsed;
                    break;

                default:
                    pwmSliders[pin].Visibility = Visibility.Collapsed;
                    pwmTextBlocks[pin].Visibility = Visibility.Visible;
                    break;
            }
        }

        /// <summary>
        /// displays a toast with the given heading, body, and optional second body
        /// </summary>
        /// <param name="heading">A required heading</param>
        /// <param name="body">A required body</param>
        /// <param name="body2">an optional second body</param>
        private void ShowToast( string heading, string body, string body2 )
        {
            var builder = new StringBuilder();
            builder.Append( "<toast><visual version='1'><binding template='ToastText04'><text id='1'>" )
                .Append( heading )
                .Append( "</text><text id='2'>" )
                .Append( body )
                .Append( "</text>" );

            if( !string.IsNullOrEmpty( body2 ) )
            {
                builder.Append( "<text id='3'>" )
                    .Append( body2 )
                    .Append( "</text>" );
            }

            builder.Append( "</binding>" )
                .Append( "</visual>" )
                .Append( "</toast>" );

            var toastDom = new Windows.Data.Xml.Dom.XmlDocument();
            toastDom.LoadXml( builder.ToString() );
            var toast = new ToastNotification( toastDom );
            try
            {
                ToastNotificationManager.CreateToastNotifier().Show( toast );
            }
            catch( Exception )
            {
                //do nothing, toast will gracefully fail
            }
        }


        //******************************************************************************
        //* Utility Functions
        //******************************************************************************

        /// <summary>
        /// retrieves the pin number associated with a button object
        /// </summary>
        /// <param name="button">the button to retrieve a pin number from</param>
        /// <returns>the pin number</returns>
        private byte GetPinFromButtonObject( ToggleSwitch button )
        {
            return Convert.ToByte( button.Name.Substring( button.Name.IndexOf( '_' ) + 1 ) );
        }

        /// <summary>
        /// Parses a string to retrieve an int value. Strings may be in hex (0x??)
        /// binary (0b????????) or decimal. Leading 0 not necessary.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        private uint ParsePositiveDecimalValueOrThrow( string text )
        {
            if( string.IsNullOrEmpty( text ) ) throw new FormatException();

            int val;

            //did they enter a number in binary or hex format?
            if( text.ToLower().Contains( "x" ) )
            {
                val = Convert.ToInt32( text.Substring( text.IndexOf( "x" ) + 1 ), 16 );
            }
            else if( text.ToLower().Contains( "b" ) )
            {
                val = Convert.ToInt32( text.Substring( text.IndexOf( 'b' ) + 1 ), 2 );
            }
            else
            {
                val = Convert.ToInt32( text );
            }

            if( val < 0 ) throw new FormatException();

            return (uint)val;
        }


        /// <summary>
        /// Arduino numbers their analog pins directly after the digital pins. Meaning A0 is actally pin 14 on an Uno,
        /// because there are 14 digital pins on an Uno. Therefore, when we're working with functions that don't know the
        /// difference between Analog and Digital pin numbers, we need to convert pin 0 (meaning A0) into pin + numberOfDigitalPins
        /// </summary>
        /// <param name="pin"></param>
        /// <returns></returns>
        private byte ConvertAnalogPinToPinNumber( byte pin )
        {
            return (byte)( pin + numberOfDigitalPins );
        }

        /// <summary>
        /// This function sends a single Analog telemetry event
        /// </summary>
        /// <param name="pin">the pin number to be reported</param>
        /// <param name="value">the value of the pin</param>
        private void SendAnalogTelemetryEvent(byte pin, double value)
        {
            var properties = new Dictionary<string, string>();
            properties.Add("pin_number", pin.ToString());
            properties.Add("analog_value", value.ToString());
            App.Telemetry.TrackEvent("Analog_Slider_Value_Changed", properties);
        }

        /// <summary>
        /// This function sends a single PWM telemetry event
        /// </summary>
        /// <param name="pin">the pin number to be reported</param>
        /// <param name="value">the value of the pin</param>
        private void SendPwmTelemetryEvent( byte pin, double value )
        {
            var properties = new Dictionary<string, string>();
            properties.Add( "pin_number", pin.ToString() );
            properties.Add( "analog_value", value.ToString() );
            App.Telemetry.TrackEvent( "Pwm_Slider_Value_Changed", properties );
        }


        //******************************************************************************
        //* Menu Button Click Events
        //******************************************************************************

        /// <summary>
        /// Called if the Analog button is pressed
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void ConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            lastPivotNavigationTime = DateTime.Now;
            App.Telemetry.TrackMetric("Pivot_" + pages[currentPage] + "_Time_Spent_In_Seconds", (DateTime.Now - lastPivotNavigationTime).TotalSeconds);

            this.Frame.Navigate(typeof(ConnectionPage));
        }

        /// <summary>
        /// Called if the Analog button is pressed
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void DigitalButton_Click(object sender, RoutedEventArgs e)
        {
            DigitalScroll.Visibility = Visibility.Visible;
            AnalogScroll.Visibility = Visibility.Collapsed;
            PWMScroll.Visibility = Visibility.Collapsed;
            AboutPanel.Visibility = Visibility.Collapsed;

            DigitalRectangle.Visibility = Visibility.Visible;
            AnalogRectangle.Visibility = Visibility.Collapsed;
            PWMRectangle.Visibility = Visibility.Collapsed;
            AboutRectangle.Visibility = Visibility.Collapsed;

            DigitalText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 14, 127, 217));
            AnalogText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            PWMText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            AboutText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);

            for (byte pin = 0; pin < numberOfDigitalPins; ++pin)
            {
                UpdateDigitalPinIndicators(pin);
            }

            App.Telemetry.TrackPageView("Digital_Controls_Page");
            lastPivotNavigationTime = DateTime.Now;

            App.Telemetry.TrackMetric("Pivot_" + pages[currentPage] + "_Time_Spent_In_Seconds", (DateTime.Now - lastPivotNavigationTime).TotalSeconds);

            currentPage = 0;
        }

        /// <summary>
        /// Called if the Analog button is pressed
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void AnalogButton_Click(object sender, RoutedEventArgs e)
        {
            DigitalScroll.Visibility = Visibility.Collapsed;
            AnalogScroll.Visibility = Visibility.Visible;
            PWMScroll.Visibility = Visibility.Collapsed;
            AboutPanel.Visibility = Visibility.Collapsed;

            DigitalRectangle.Visibility = Visibility.Collapsed;
            AnalogRectangle.Visibility = Visibility.Visible;
            PWMRectangle.Visibility = Visibility.Collapsed;
            AboutRectangle.Visibility = Visibility.Collapsed;

            DigitalText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            AnalogText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 14, 127, 217));
            PWMText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            AboutText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);

            for (byte pin = 0; pin < numberOfAnalogPins; ++pin)
            {
                UpdateAnalogPinModeIndicator(pin);
            }

            App.Telemetry.TrackPageView("Analog_Controls_Page");
            lastPivotNavigationTime = DateTime.Now;

            App.Telemetry.TrackMetric("Pivot_" + pages[currentPage] + "_Time_Spent_In_Seconds", (DateTime.Now - lastPivotNavigationTime).TotalSeconds);

            currentPage = 1;
        }

        /// <summary>
        /// Called if the Analog button is pressed
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void PWMButton_Click(object sender, RoutedEventArgs e)
        {
            DigitalScroll.Visibility = Visibility.Collapsed;
            AnalogScroll.Visibility = Visibility.Collapsed;
            PWMScroll.Visibility = Visibility.Visible;
            AboutPanel.Visibility = Visibility.Collapsed;

            DigitalRectangle.Visibility = Visibility.Collapsed;
            AnalogRectangle.Visibility = Visibility.Collapsed;
            PWMRectangle.Visibility = Visibility.Visible;
            AboutRectangle.Visibility = Visibility.Collapsed;

            DigitalText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            AnalogText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            PWMText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 14, 127, 217));
            AboutText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);

            for (byte pin = 0; pin < numberOfAnalogPins; ++pin)
            {
                UpdatePwmPinModeIndicator(pin);
            }

            App.Telemetry.TrackPageView("PWM_Controls_Page");
            lastPivotNavigationTime = DateTime.Now;

            App.Telemetry.TrackMetric("Pivot_" + pages[currentPage] + "_Time_Spent_In_Seconds", (DateTime.Now - lastPivotNavigationTime).TotalSeconds);

            currentPage = 2;
        }

        /// <summary>
        /// Called if the Analog button is pressed
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            DigitalScroll.Visibility = Visibility.Collapsed;
            AnalogScroll.Visibility = Visibility.Collapsed;
            PWMScroll.Visibility = Visibility.Collapsed;
            AboutPanel.Visibility = Visibility.Visible;

            DigitalRectangle.Visibility = Visibility.Collapsed;
            AnalogRectangle.Visibility = Visibility.Collapsed;
            PWMRectangle.Visibility = Visibility.Collapsed;
            AboutRectangle.Visibility = Visibility.Visible;

            DigitalText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            AnalogText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            PWMText.Foreground = new SolidColorBrush(Windows.UI.Colors.Black);
            AboutText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 14, 127, 217));

            App.Telemetry.TrackPageView("About_Page");
            lastPivotNavigationTime = DateTime.Now;

            App.Telemetry.TrackMetric("Pivot_" + pages[currentPage] + "_Time_Spent_In_Seconds", (DateTime.Now - lastPivotNavigationTime).TotalSeconds);

            currentPage = 3;
        }

        // <summary>
        /// Called if the pointer hovers over the Connection button.
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void ConnectionButton_Enter(object sender, RoutedEventArgs e)
        {
            ConnectionRectangle.Visibility = Visibility.Visible;
        }

        // <summary>
        /// Called if the pointer hovers over the Digital button.
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void DigitalButton_Enter(object sender, RoutedEventArgs e)
        {
            DigitalRectangle.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Called if the pointer hovers over the Analog button.
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void AnalogButton_Enter(object sender, RoutedEventArgs e)
        {
            AnalogRectangle.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Called if the pointer hovers over the PWM button.
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void PWMButton_Enter(object sender, RoutedEventArgs e)
        {
            PWMRectangle.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Called if the pointer hovers over the About button.
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void AboutButton_Enter(object sender, RoutedEventArgs e)
        {
            AboutRectangle.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Called if the pointer exits the boundaries of any button.
        /// </summary>
        /// <param name="sender">The object invoking the event</param>
        /// <param name="e">Arguments relating to the event</param>
        private void Button_Exit(object sender, RoutedEventArgs e)
        {
            ConnectionRectangle.Visibility = Visibility.Collapsed;
            DigitalRectangle.Visibility = (currentPage == 0) ? Visibility.Visible : Visibility.Collapsed;
            AnalogRectangle.Visibility = (currentPage == 1) ? Visibility.Visible : Visibility.Collapsed;
            PWMRectangle.Visibility = (currentPage == 2) ? Visibility.Visible : Visibility.Collapsed;
            AboutRectangle.Visibility = (currentPage == 3) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
