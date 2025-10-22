using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BrailleDisplay.SDK
{
    // ==================== ENUMS AND EVENT ARGS ====================

    /// <summary>
    /// Represents the physical buttons on the braille device
    /// </summary>
    public enum BrailleButton
    {
        Select,
        NextElement,
        PreviousElement,
        Back,
        ToggleBookmark,
        NextBookmark
    }

    /// <summary>
    /// Event arguments for button press events
    /// </summary>
    public class ButtonEventArgs : EventArgs
    {
        public BrailleButton Button { get; }
        public DateTime Timestamp { get; }
        public bool Handled { get; set; }

        public ButtonEventArgs(BrailleButton button)
        {
            Button = button;
            Timestamp = DateTime.Now;
            Handled = false;
        }
    }

    /// <summary>
    /// Event arguments for navigation events
    /// </summary>
    public class NavigationEventArgs : EventArgs
    {
        public string CurrentElementText { get; set; }
        public int CurrentLevel { get; set; }
        public bool CanGoBack { get; set; }
        public bool CanGoNext { get; set; }
        public bool CanGoPrevious { get; set; }
    }

    /// <summary>
    /// Represents a bookmark location in the application
    /// </summary>
    public class Bookmark
    {
        public string ApplicationName { get; set; }
        public string Location { get; set; }
        public string DisplayText { get; set; }
        public DateTime Created { get; set; }
        public object CustomData { get; set; }

        public Bookmark(string appName, string location, string displayText)
        {
            ApplicationName = appName;
            Location = location;
            DisplayText = displayText;
            Created = DateTime.Now;
        }
    }

    // ==================== HARDWARE CONTROLLER ====================

    /// <summary>
    /// Low-level hardware communication with the Arduino-based braille display
    /// </summary>
    public class BrailleHardwareController : IDisposable
{
    private SerialPort _serialPort;
    private readonly object _lock = new object();
    private Thread _readThread;
    private bool _isRunning;
    private bool _isSimulationMode = false;

    // Protocol bytes for Arduino communication
    private const byte CMD_SET_CELL = 0x01;
    private const byte CMD_CLEAR_ALL = 0x02;
    private const byte CMD_SET_ROW = 0x03;
    private const byte BUTTON_PRESSED = 0x10;

    public event EventHandler<BrailleButton> ButtonReceived;

    /// <summary>
    /// Initialize hardware connection to Arduino
    /// </summary>
    /// <param name="portName">COM port (e.g., "COM3")</param>
    /// <param name="baudRate">Baud rate (default 9600)</param>
    public BrailleHardwareController(string portName = "COM3", int baudRate = 9600)
    {
        try
        {
            _serialPort = new SerialPort(portName, baudRate)
            {
                ReadTimeout = 500,
                WriteTimeout = 500
            };

            _serialPort.Open();

            // Start thread to read button inputs from Arduino
            _isRunning = true;
            _readThread = new Thread(ReadFromArduino)
            {
                IsBackground = true,
                Name = "Arduino Read Thread"
            };
            _readThread.Start();

            Console.WriteLine($"✓ Connected to braille display on {portName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  No braille display found on {portName}");
            Console.WriteLine($"   Error: {ex.Message}");
            Console.WriteLine("   Running in SIMULATION MODE (console only)\n");
            _isSimulationMode = true;
            _isRunning = false;
            // Don't start the read thread if port failed to open
        }
    }

    /// <summary>
    /// Continuously read from Arduino for button presses
    /// Protocol: [BUTTON_PRESSED][BUTTON_ID]
    /// </summary>
    private void ReadFromArduino()
    {
        while (_isRunning)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen && _serialPort.BytesToRead >= 2)
                {
                    byte[] buffer = new byte[2];
                    _serialPort.Read(buffer, 0, 2);

                    if (buffer[0] == BUTTON_PRESSED)
                    {
                        BrailleButton button = (BrailleButton)buffer[1];
                        ButtonReceived?.Invoke(this, button);
                    }
                }
                Thread.Sleep(10); // Small delay to prevent CPU spinning
            }
            catch (TimeoutException)
            {
                // No data available, continue
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino read error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Set a single braille cell
    /// Protocol: [CMD_SET_CELL][ROW][COL][DOTS]
    /// </summary>
    /// <param name="row">Row index (0-based)</param>
    /// <param name="column">Column index (0-based)</param>
    /// <param name="dots">8-bit dot pattern (bit 0 = dot 1, bit 7 = dot 8)</param>
    public void SetCell(int row, int column, byte dots)
    {
        if (_isSimulationMode)
            return; // Skip in simulation mode

        if (_serialPort != null && _serialPort.IsOpen)
        {
            lock (_lock)
            {
                try
                {
                    byte[] command = new byte[]
                    {
                        CMD_SET_CELL,
                        (byte)row,
                        (byte)column,
                        dots
                    };
                    _serialPort.Write(command, 0, command.Length);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SetCell error: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Set an entire row of braille cells at once (more efficient)
    /// Protocol: [CMD_SET_ROW][ROW][LENGTH][DOTS...]
    /// </summary>
    public void SetRow(int row, byte[] dotPatterns)
    {
        if (_isSimulationMode)
            return; // Skip in simulation mode

        if (_serialPort != null && _serialPort.IsOpen)
        {
            lock (_lock)
            {
                try
                {
                    byte[] command = new byte[3 + dotPatterns.Length];
                    command[0] = CMD_SET_ROW;
                    command[1] = (byte)row;
                    command[2] = (byte)dotPatterns.Length;
                    Array.Copy(dotPatterns, 0, command, 3, dotPatterns.Length);

                    _serialPort.Write(command, 0, command.Length);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SetRow error: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Clear all pins (set all to 0)
    /// Protocol: [CMD_CLEAR_ALL]
    /// </summary>
    public void ClearAll()
    {
        if (_isSimulationMode)
            return; // Skip in simulation mode

        if (_serialPort != null && _serialPort.IsOpen)
        {
            lock (_lock)
            {
                try
                {
                    _serialPort.Write(new byte[] { CMD_CLEAR_ALL }, 0, 1);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ClearAll error: {ex.Message}");
                }
            }
        }
    }

    public void Dispose()
    {
        _isRunning = false;
        
        if (_readThread != null && _readThread.IsAlive)
        {
            _readThread.Join(1000); // Wait up to 1 second for thread to finish
        }

        if (_serialPort != null && _serialPort.IsOpen)
        {
            try
            {
                ClearAll(); // Clear display on disconnect
                _serialPort.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dispose error: {ex.Message}");
            }
            finally
            {
                _serialPort.Dispose();
            }
        }
    }

    /// <summary>
    /// Auto-detect available COM ports with braille devices
    /// </summary>
    public static string[] FindBrailleDevices()
    {
        return SerialPort.GetPortNames();
    }
}

    // ==================== BRAILLE TRANSLATOR ====================

    /// <summary>
    /// Handles text-to-braille conversion
    /// </summary>
    public static class BrailleTranslator
    {
        // Grade 1 Braille ASCII mapping (6-dot pattern)
        // Bit positions: 0=dot1, 1=dot2, 2=dot3, 3=dot4, 4=dot5, 5=dot6
        private static readonly Dictionary<char, byte> _asciiToBraille = new Dictionary<char, byte>
        {
            // Letters
            {'a', 0x01}, {'b', 0x03}, {'c', 0x09}, {'d', 0x19}, {'e', 0x11},
            {'f', 0x0B}, {'g', 0x1B}, {'h', 0x13}, {'i', 0x0A}, {'j', 0x1A},
            {'k', 0x05}, {'l', 0x07}, {'m', 0x0D}, {'n', 0x1D}, {'o', 0x15},
            {'p', 0x0F}, {'q', 0x1F}, {'r', 0x17}, {'s', 0x0E}, {'t', 0x1E},
            {'u', 0x25}, {'v', 0x27}, {'w', 0x3A}, {'x', 0x2D}, {'y', 0x3D},
            {'z', 0x35},
            
            // Numbers (with number prefix in real implementation)
            {'1', 0x01}, {'2', 0x03}, {'3', 0x09}, {'4', 0x19}, {'5', 0x11},
            {'6', 0x0B}, {'7', 0x1B}, {'8', 0x13}, {'9', 0x0A}, {'0', 0x1A},
            
            // Common punctuation
            {' ', 0x00}, {',', 0x02}, {';', 0x06}, {':', 0x12}, {'.', 0x2C},
            {'?', 0x26}, {'!', 0x16}, {'\'', 0x04}, {'-', 0x24},
        };

        /// <summary>
        /// Convert text to braille dot patterns
        /// </summary>
        public static byte[] TextToBraille(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<byte>();

            List<byte> result = new List<byte>();

            foreach (char c in text.ToLower())
            {
                if (_asciiToBraille.TryGetValue(c, out byte dots))
                {
                    result.Add(dots);
                }
                else
                {
                    result.Add(0x00); // Unknown character = blank
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Convert individual character to braille
        /// </summary>
        public static byte CharToBraille(char c)
        {
            return _asciiToBraille.TryGetValue(char.ToLower(c), out byte dots) ? dots : (byte)0x00;
        }

        /// <summary>
        /// Create a dot pattern from individual dot states
        /// </summary>
        public static byte CreateDotPattern(bool dot1, bool dot2, bool dot3,
                                           bool dot4, bool dot5, bool dot6,
                                           bool dot7 = false, bool dot8 = false)
        {
            byte pattern = 0;
            if (dot1) pattern |= 0x01;
            if (dot2) pattern |= 0x02;
            if (dot3) pattern |= 0x04;
            if (dot4) pattern |= 0x08;
            if (dot5) pattern |= 0x10;
            if (dot6) pattern |= 0x20;
            if (dot7) pattern |= 0x40;
            if (dot8) pattern |= 0x80;
            return pattern;
        }
    }

    // ==================== BOOKMARK MANAGER ====================

    /// <summary>
    /// Manages bookmarks for quick navigation
    /// </summary>
    public class BookmarkManager
    {
        private List<Bookmark> _bookmarks = new List<Bookmark>();
        private int _currentIndex = -1;
        private string _currentApplication;

        public IReadOnlyList<Bookmark> Bookmarks => _bookmarks.AsReadOnly();
        public int CurrentIndex => _currentIndex;

        public BookmarkManager(string applicationName)
        {
            _currentApplication = applicationName;
        }

        /// <summary>
        /// Toggle bookmark at current location
        /// If bookmark exists, remove it. If not, add it.
        /// </summary>
        public bool ToggleBookmark(string location, string displayText)
        {
            var existing = _bookmarks.FirstOrDefault(b =>
                b.ApplicationName == _currentApplication &&
                b.Location == location);

            if (existing != null)
            {
                _bookmarks.Remove(existing);
                // Adjust current index if needed
                if (_currentIndex >= _bookmarks.Count)
                    _currentIndex = _bookmarks.Count - 1;
                return false; // Bookmark removed
            }
            else
            {
                _bookmarks.Add(new Bookmark(_currentApplication, location, displayText));
                return true; // Bookmark added
            }
        }

        /// <summary>
        /// Navigate to next bookmark
        /// </summary>
        public Bookmark NextBookmark()
        {
            if (_bookmarks.Count == 0)
                return null;

            _currentIndex = (_currentIndex + 1) % _bookmarks.Count;
            return _bookmarks[_currentIndex];
        }

        /// <summary>
        /// Navigate to previous bookmark
        /// </summary>
        public Bookmark PreviousBookmark()
        {
            if (_bookmarks.Count == 0)
                return null;

            _currentIndex--;
            if (_currentIndex < 0)
                _currentIndex = _bookmarks.Count - 1;

            return _bookmarks[_currentIndex];
        }

        /// <summary>
        /// Clear all bookmarks for current application
        /// </summary>
        public void ClearBookmarks()
        {
            _bookmarks.RemoveAll(b => b.ApplicationName == _currentApplication);
            _currentIndex = -1;
        }

        /// <summary>
        /// Get bookmark at specific location
        /// </summary>
        public Bookmark GetBookmark(string location)
        {
            return _bookmarks.FirstOrDefault(b =>
                b.ApplicationName == _currentApplication &&
                b.Location == location);
        }
    }

    // ==================== MAIN DEVICE CLASS ====================

    /// <summary>
    /// Main interface for controlling the braille display device
    /// Provides high-level API for developers
    /// </summary>
    public class BrailleDisplayDevice : IDisposable
    {
        private readonly BrailleHardwareController _hardware;
        private readonly int _rows;
        private readonly int _columns;
        private byte[,] _currentDisplay;
        private readonly BookmarkManager _bookmarkManager;

        // Navigation state
        private Stack<string> _navigationStack = new Stack<string>();
        private string _currentElement;

        // Button handler delegates
        private Func<Task> _selectHandler;
        private Func<Task> _nextElementHandler;
        private Func<Task> _previousElementHandler;
        private Func<Task> _backHandler;
        private Func<Task> _toggleBookmarkHandler;
        private Func<Task> _nextBookmarkHandler;

        // Events
        public event EventHandler<ButtonEventArgs> ButtonPressed;
        public event EventHandler<NavigationEventArgs> NavigationChanged;

        // Properties
        public int Rows => _rows;
        public int Columns => _columns;
        public BookmarkManager Bookmarks => _bookmarkManager;
        public string CurrentElement => _currentElement;
        public bool CanGoBack => _navigationStack.Count > 0;

        /// <summary>
        /// Initialize the braille display device
        /// </summary>
        /// <param name="rows">Number of rows on the display</param>
        /// <param name="columns">Number of columns (cells) per row</param>
        /// <param name="applicationName">Name of the application using this device</param>
        /// <param name="portName">COM port for Arduino connection</param>
        public BrailleDisplayDevice(int rows, int columns, string applicationName, string portName = "COM3")
        {
            _rows = rows;
            _columns = columns;
            _currentDisplay = new byte[rows, columns];
            _bookmarkManager = new BookmarkManager(applicationName);

            // Initialize hardware
            _hardware = new BrailleHardwareController(portName);
            _hardware.ButtonReceived += OnButtonReceived;

            // Set up default button handlers
            SetupDefaultHandlers();
        }

        /// <summary>
        /// Set up default implementations for all buttons
        /// </summary>
        private void SetupDefaultHandlers()
        {
            _selectHandler = async () => {
                // Default: do nothing, let application handle
                await Task.CompletedTask;
            };

            _nextElementHandler = async () => {
                // Default: notify that next element requested
                await Task.CompletedTask;
            };

            _previousElementHandler = async () => {
                // Default: notify that previous element requested
                await Task.CompletedTask;
            };

            _backHandler = async () => {
                // Default: pop navigation stack
                if (_navigationStack.Count > 0)
                {
                    _currentElement = _navigationStack.Pop();
                    OnNavigationChanged();
                }
                await Task.CompletedTask;
            };

            _toggleBookmarkHandler = async () => {
                // Default: toggle bookmark at current element
                if (!string.IsNullOrEmpty(_currentElement))
                {
                    bool added = _bookmarkManager.ToggleBookmark(
                        _currentElement,
                        _currentElement);

                    DisplayText(added ? "Bookmark added" : "Bookmark removed");
                    await Task.Delay(1000);
                }
                await Task.CompletedTask;
            };

            _nextBookmarkHandler = async () => {
                // Default: jump to next bookmark
                var bookmark = _bookmarkManager.NextBookmark();
                if (bookmark != null)
                {
                    NavigateTo(bookmark.Location);
                    DisplayText(bookmark.DisplayText);
                }
                await Task.CompletedTask;
            };
        }

        /// <summary>
        /// Handle button press from hardware
        /// </summary>
        private async void OnButtonReceived(object sender, BrailleButton button)
        {
            var args = new ButtonEventArgs(button);

            // Fire event to allow application to handle
            ButtonPressed?.Invoke(this, args);

            // If not handled by application, use default handler
            if (!args.Handled)
            {
                await ExecuteDefaultHandler(button);
            }
        }

        /// <summary>
        /// Execute the default handler for a button
        /// </summary>
        private async Task ExecuteDefaultHandler(BrailleButton button)
        {
            switch (button)
            {
                case BrailleButton.Select:
                    await _selectHandler();
                    break;
                case BrailleButton.NextElement:
                    await _nextElementHandler();
                    break;
                case BrailleButton.PreviousElement:
                    await _previousElementHandler();
                    break;
                case BrailleButton.Back:
                    await _backHandler();
                    break;
                case BrailleButton.ToggleBookmark:
                    await _toggleBookmarkHandler();
                    break;
                case BrailleButton.NextBookmark:
                    await _nextBookmarkHandler();
                    break;
            }
        }

        // ==================== BUTTON HANDLER OVERRIDE METHODS ====================

        /// <summary>
        /// Override the Select button behavior
        /// </summary>
        public void SetSelectHandler(Func<Task> handler)
        {
            _selectHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Override the Next Element button behavior
        /// </summary>
        public void SetNextElementHandler(Func<Task> handler)
        {
            _nextElementHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Override the Previous Element button behavior
        /// </summary>
        public void SetPreviousElementHandler(Func<Task> handler)
        {
            _previousElementHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Override the Back button behavior
        /// </summary>
        public void SetBackHandler(Func<Task> handler)
        {
            _backHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Override the Toggle Bookmark button behavior
        /// </summary>
        public void SetToggleBookmarkHandler(Func<Task> handler)
        {
            _toggleBookmarkHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Override the Next Bookmark button behavior
        /// </summary>
        public void SetNextBookmarkHandler(Func<Task> handler)
        {
            _nextBookmarkHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Reset all button handlers to defaults
        /// </summary>
        public void ResetButtonHandlers()
        {
            SetupDefaultHandlers();
        }

        // ==================== DISPLAY METHODS ====================

        /// <summary>
        /// Display text on the braille display with automatic conversion
        /// </summary>
        public void DisplayText(string text, int startRow = 0, int startColumn = 0)
        {
            if (string.IsNullOrEmpty(text))
            {
                Clear();
                return;
            }

            byte[] brailleCells = BrailleTranslator.TextToBraille(text);

            int row = startRow;
            int col = startColumn;

            foreach (byte cell in brailleCells)
            {
                if (col >= _columns)
                {
                    col = 0;
                    row++;
                }
                if (row >= _rows) break;

                SetCell(row, col, cell);
                col++;
            }

            // Clear remaining cells in the last used row
            while (col < _columns && row < _rows)
            {
                SetCell(row, col, 0);
                col++;
            }
        }

        /// <summary>
        /// Set a single braille cell using dot pattern byte
        /// </summary>
        public void SetCell(int row, int column, byte brailleDots)
        {
            if (row < 0 || row >= _rows || column < 0 || column >= _columns)
                throw new ArgumentOutOfRangeException("Row or column out of range");

            _currentDisplay[row, column] = brailleDots;
            _hardware.SetCell(row, column, brailleDots);
        }

        /// <summary>
        /// Set a single braille cell using individual dot states (more intuitive)
        /// </summary>
        public void SetCell(int row, int column, bool dot1, bool dot2, bool dot3,
                          bool dot4, bool dot5, bool dot6, bool dot7 = false, bool dot8 = false)
        {
            byte dots = BrailleTranslator.CreateDotPattern(dot1, dot2, dot3, dot4, dot5, dot6, dot7, dot8);
            SetCell(row, column, dots);
        }

        /// <summary>
        /// Set an entire row at once (more efficient for large updates)
        /// </summary>
        public void SetRow(int row, byte[] dotPatterns)
        {
            if (row < 0 || row >= _rows)
                throw new ArgumentOutOfRangeException(nameof(row));

            if (dotPatterns.Length > _columns)
                throw new ArgumentException($"Pattern array too long for display (max {_columns})");

            for (int col = 0; col < dotPatterns.Length; col++)
            {
                _currentDisplay[row, col] = dotPatterns[col];
            }

            _hardware.SetRow(row, dotPatterns);
        }

        /// <summary>
        /// Clear the entire display
        /// </summary>
        public void Clear()
        {
            Array.Clear(_currentDisplay, 0, _currentDisplay.Length);
            _hardware.ClearAll();
        }

        /// <summary>
        /// Get the current state of a cell
        /// </summary>
        public byte GetCell(int row, int column)
        {
            if (row < 0 || row >= _rows || column < 0 || column >= _columns)
                throw new ArgumentOutOfRangeException();

            return _currentDisplay[row, column];
        }

        // ==================== NAVIGATION METHODS ====================

        /// <summary>
        /// Navigate to a new element (adds to history stack)
        /// </summary>
        public void NavigateTo(string elementIdentifier)
        {
            if (!string.IsNullOrEmpty(_currentElement))
            {
                _navigationStack.Push(_currentElement);
            }

            _currentElement = elementIdentifier;
            OnNavigationChanged();
        }

        /// <summary>
        /// Go back to previous element
        /// </summary>
        public void NavigateBack()
        {
            if (_navigationStack.Count > 0)
            {
                _currentElement = _navigationStack.Pop();
                OnNavigationChanged();
            }
        }

        /// <summary>
        /// Clear navigation history
        /// </summary>
        public void ClearNavigationHistory()
        {
            _navigationStack.Clear();
        }

        /// <summary>
        /// Raise navigation changed event
        /// </summary>
        private void OnNavigationChanged()
        {
            NavigationChanged?.Invoke(this, new NavigationEventArgs
            {
                CurrentElementText = _currentElement,
                CurrentLevel = _navigationStack.Count,
                CanGoBack = _navigationStack.Count > 0
            });
        }

        // ==================== CLEANUP ====================

        public void Dispose()
        {
            Clear();
            _hardware?.Dispose();
        }
    }
}