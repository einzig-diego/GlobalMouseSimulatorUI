using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace GlobalMouseSimulatorUI
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    /// <summary>
    /// Represents the configuration settings for the mouse simulator.
    /// </summary>
    [Serializable]
    public class Configuration
    {
        public Keys KeyLeft { get; set; } = Keys.A;
        public Keys KeyRight { get; set; } = Keys.D;
        public Keys KeyUp { get; set; } = Keys.W;
        public Keys KeyDown { get; set; } = Keys.S;
        public Keys KeyClickLeft { get; set; } = Keys.E; // Key for simulating left mouse click
        public Keys KeyClickRight { get; set; } = Keys.Q; // Key for simulating right mouse click
        public int MoveAmount { get; set; } = 10; // Pixels to move per key press
        public int PollingInterval { get; set; } = 20; // Milliseconds between key checks
        public bool IsEnabled { get; set; } = false; // Whether the mouse simulation is enabled
    }

    /// <summary>
    /// Main form for the mouse simulator application.
    /// </summary>
    public partial class MainForm : Form
    {
        private Configuration config = new Configuration();
        private Dictionary<Keys, DateTime> lastProcessed = new Dictionary<Keys, DateTime>();

        private CheckBox chkEnable;
        private Button btnLeft, btnRight, btnUp, btnDown, btnClickLeft, btnClickRight, btnSave;
        private NumericUpDown numMoveAmount, numPollingInterval;
        private Label lblMoveAmount, lblPollingInterval;

        private Keys? remapTarget = null;
        private Thread keyPollingThread;
        private bool isPolling = false;

        public MainForm()
        {
            LoadConfiguration(); // Load configuration before initializing the UI
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Mouse Simulator Configuration";
            this.Width = 400;
            this.Height = 400;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            // Enable/Disable checkbox
            chkEnable = new CheckBox
            {
                Text = "Enable Mouse Simulation",
                Left = 20,
                Top = 20,
                AutoSize = true
            };
            chkEnable.CheckedChanged += ChkEnable_CheckedChanged;
            this.Controls.Add(chkEnable);

            // Instruction label
            Label lblInstruction = new Label
            {
                Text = "Click a button to remap a key. Then press the desired key.",
                Left = 20,
                Top = 50,
                Width = 350
            };
            this.Controls.Add(lblInstruction);

            // Buttons for key remapping
            btnLeft = new Button { Left = 20, Top = 80, Width = 150 };
            btnLeft.Click += (s, e) => BeginRemap(config.KeyLeft, btnLeft);
            this.Controls.Add(btnLeft);

            btnRight = new Button { Left = 200, Top = 80, Width = 150 };
            btnRight.Click += (s, e) => BeginRemap(config.KeyRight, btnRight);
            this.Controls.Add(btnRight);

            btnUp = new Button { Left = 20, Top = 120, Width = 150 };
            btnUp.Click += (s, e) => BeginRemap(config.KeyUp, btnUp);
            this.Controls.Add(btnUp);

            btnDown = new Button { Left = 200, Top = 120, Width = 150 };
            btnDown.Click += (s, e) => BeginRemap(config.KeyDown, btnDown);
            this.Controls.Add(btnDown);

            btnClickLeft = new Button { Left = 20, Top = 160, Width = 150 };
            btnClickLeft.Click += (s, e) => BeginRemap(config.KeyClickLeft, btnClickLeft);
            this.Controls.Add(btnClickLeft);

            btnClickRight = new Button { Left = 200, Top = 160, Width = 150 };
            btnClickRight.Click += (s, e) => BeginRemap(config.KeyClickRight, btnClickRight);
            this.Controls.Add(btnClickRight);

            // NumericUpDown for movement amount
            numMoveAmount = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 100,
                Value = config.MoveAmount,
                Left = 20,
                Top = 200,
                Width = 100
            };
            numMoveAmount.ValueChanged += (s, e) => config.MoveAmount = (int)numMoveAmount.Value;
            this.Controls.Add(numMoveAmount);

            lblMoveAmount = new Label
            {
                Text = "Movement Amount (Pixels):",
                Left = 130,
                Top = 200,
                AutoSize = true
            };
            this.Controls.Add(lblMoveAmount);

            // NumericUpDown for polling interval
            numPollingInterval = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 1000,
                Value = config.PollingInterval,
                Left = 20,
                Top = 240,
                Width = 100
            };
            numPollingInterval.ValueChanged += (s, e) => config.PollingInterval = (int)numPollingInterval.Value;
            this.Controls.Add(numPollingInterval);

            lblPollingInterval = new Label
            {
                Text = "Polling Interval (Milliseconds):",
                Left = 130,
                Top = 240,
                AutoSize = true
            };
            this.Controls.Add(lblPollingInterval);

            // Save button
            btnSave = new Button
            {
                Text = "Save Configuration",
                Left = 20,
                Top = 280,
                Width = 150
            };
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);

            // Set the checkbox state based on loaded configuration
            chkEnable.Checked = config.IsEnabled;

            // Update button texts with loaded configuration
            UpdateButtonTexts();

            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;
        }

        /// <summary>
        /// Updates the text of the buttons to reflect the current key bindings.
        /// </summary>
        private void UpdateButtonTexts()
        {
            btnLeft.Text = "Left: " + config.KeyLeft.ToString();
            btnRight.Text = "Right: " + config.KeyRight.ToString();
            btnUp.Text = "Up: " + config.KeyUp.ToString();
            btnDown.Text = "Down: " + config.KeyDown.ToString();
            btnClickLeft.Text = "Click Left: " + config.KeyClickLeft.ToString();
            btnClickRight.Text = "Click Right: " + config.KeyClickRight.ToString();
        }

        /// <summary>
        /// Handles the event when the enable checkbox state changes.
        /// </summary>
        private void ChkEnable_CheckedChanged(object sender, EventArgs e)
        {
            if (chkEnable.Checked)
            {
                config.IsEnabled = true;
                StartPolling();
            }
            else
            {
                config.IsEnabled = false;
                StopPolling();
            }
        }

        /// <summary>
        /// Starts the key polling thread.
        /// </summary>
        private void StartPolling()
        {
            if (keyPollingThread == null || !keyPollingThread.IsAlive)
            {
                isPolling = true;
                keyPollingThread = new Thread(PollKeys);
                keyPollingThread.Start();
            }
        }

        /// <summary>
        /// Stops the key polling thread.
        /// </summary>
        private void StopPolling()
        {
            isPolling = false;
            if (keyPollingThread != null && keyPollingThread.IsAlive)
            {
                keyPollingThread.Join();
            }
        }

        /// <summary>
        /// Polls the keys to check if they are pressed and performs the corresponding actions.
        /// </summary>
        private void PollKeys()
        {
            while (isPolling)
            {
                if (config.IsEnabled)
                {
                    CheckKey(config.KeyLeft, () => MoveMouse(-config.MoveAmount, 0));
                    CheckKey(config.KeyRight, () => MoveMouse(config.MoveAmount, 0));
                    CheckKey(config.KeyUp, () => MoveMouse(0, -config.MoveAmount));
                    CheckKey(config.KeyDown, () => MoveMouse(0, config.MoveAmount));
                    CheckKey(config.KeyClickLeft, () => SimulateMouseClick(true));
                    CheckKey(config.KeyClickRight, () => SimulateMouseClick(false));
                }
                Thread.Sleep(config.PollingInterval); // Use the configured polling interval
            }
        }

        /// <summary>
        /// Checks if a key is pressed and performs the specified action.
        /// </summary>
        private void CheckKey(Keys key, Action action)
        {
            if ((GetAsyncKeyState((int)key) & 0x8000) != 0)
            {
                action();
            }
        }

        /// <summary>
        /// Begins the process of remapping a key.
        /// </summary>
        private void BeginRemap(Keys currentKey, Button button)
        {
            remapTarget = currentKey;
            button.Text = "Press new key...";
        }

        /// <summary>
        /// Handles the event when a key is pressed during key remapping.
        /// </summary>
        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (remapTarget.HasValue)
            {
                if (btnLeft.Text.Contains(remapTarget.ToString()) || btnLeft.Focused)
                {
                    config.KeyLeft = e.KeyCode;
                    btnLeft.Text = "Left: " + config.KeyLeft.ToString();
                }
                else if (btnRight.Focused)
                {
                    config.KeyRight = e.KeyCode;
                    btnRight.Text = "Right: " + config.KeyRight.ToString();
                }
                else if (btnUp.Focused)
                {
                    config.KeyUp = e.KeyCode;
                    btnUp.Text = "Up: " + config.KeyUp.ToString();
                }
                else if (btnDown.Focused)
                {
                    config.KeyDown = e.KeyCode;
                    btnDown.Text = "Down: " + config.KeyDown.ToString();
                }
                else if (btnClickLeft.Focused)
                {
                    config.KeyClickLeft = e.KeyCode;
                    btnClickLeft.Text = "Click Left: " + config.KeyClickLeft.ToString();
                }
                else if (btnClickRight.Focused)
                {
                    config.KeyClickRight = e.KeyCode;
                    btnClickRight.Text = "Click Right: " + config.KeyClickRight.ToString();
                }

                remapTarget = null;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handles the event when the save button is clicked.
        /// </summary>
        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                SaveConfiguration();
                MessageBox.Show("Configuration saved successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Saves the current configuration to a file.
        /// </summary>
        private void SaveConfiguration()
        {
            using (var stream = new FileStream("config.xml", FileMode.Create))
            {
                var serializer = new XmlSerializer(typeof(Configuration));
                serializer.Serialize(stream, config);
            }
        }

        /// <summary>
        /// Loads the configuration from a file.
        /// </summary>
        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists("config.xml"))
                {
                    using (var stream = new FileStream("config.xml", FileMode.Open))
                    {
                        var serializer = new XmlSerializer(typeof(Configuration));
                        config = (Configuration)serializer.Deserialize(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading configuration: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #region Mouse Movement and Click Simulation

        /// <summary>
        /// Moves the mouse cursor by the specified amount.
        /// </summary>
        private void MoveMouse(int dx, int dy)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dx = dx;
            inputs[0].u.mi.dy = dy;
            inputs[0].u.mi.mouseData = 0;
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE;
            inputs[0].u.mi.time = 0;
            inputs[0].u.mi.dwExtraInfo = IntPtr.Zero;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// Simulates a mouse click (left or right).
        /// </summary>
        private void SimulateMouseClick(bool isLeftClick)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.mouseData = 0;
            inputs[0].u.mi.dwFlags = isLeftClick ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_RIGHTDOWN;
            inputs[0].u.mi.time = 0;
            inputs[0].u.mi.dwExtraInfo = IntPtr.Zero;

            inputs[1].type = INPUT_MOUSE;
            inputs[1].u.mi.mouseData = 0;
            inputs[1].u.mi.dwFlags = isLeftClick ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_RIGHTUP;
            inputs[1].u.mi.time = 0;
            inputs[1].u.mi.dwExtraInfo = IntPtr.Zero;

            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        #endregion

        /// <summary>
        /// Handles the form closing event to ensure the polling thread is stopped.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopPolling();
            base.OnFormClosing(e);
        }
    }
}