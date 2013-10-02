using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.Automation;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Avi;

namespace AVU
{
    public partial class MainPage : UserControl
    {
        private static byte[] DIMENSION_CODE = new byte[] {137,68,77,78};
        private static byte[] FRAME_CODE = new byte[] {137,70,82,77};
        private static byte[] ERROR_CODE = new byte[] {137,69,82,82};
        private static byte[] STRING_SEPARATOR_CODE = new byte[] {137,69,78,68};

        private static int FRAME_HEIGHT = 320;
        private static int FRAME_WIDTH = 480;
        
        private Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private EndPoint remoteEndPoint = (EndPoint)new IPEndPoint(IPAddress.Parse("127.0.0.1"), 4506);

        private List<byte> bytes = new List<byte>();
        private Thread readerThread;
        private AviWriterJPGCompressed aviWriter;

        public MainPage()
        {
            InitializeComponent();

            App.Current.Exit += new EventHandler(CurrentExit);
        }

        private void CurrentExit(object sender, EventArgs e)
        {
            try
            {
                SendData("exit");

                if (readerThread != null)
                    readerThread.Abort();

                socket.Shutdown(SocketShutdown.Both);
                socket.Close();

                lock (aviWriter)
                {
                    if (aviWriter!= null && !aviWriter.IsClosed)
                        aviWriter.Close();
                }
            }
            catch (Exception exp)
            {
                updateDetailLogLabel("\n" + exp.Message);
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            Init():
        }

        private void Init()
        {
            try
            {
                if (App.Current.InstallState != InstallState.Installed)
                    updateDetailLogLabel("\nAlt-Click to install to local machine");

                if (AutomationFactory.IsAvailable)
                {
                    using (dynamic shell = AutomationFactory.CreateObject("WScript.Shell"))
                    {
                        shell.Run("screenshot.jar -e", 0);
                    }
                }
                else
                {
                    updateDetailLogLabel("\nUnable to launch, please run java -jar screenshot.jar -d for device and java -jar screenshot.jar -e for emulator");
                }

                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.RemoteEndPoint = remoteEndPoint;
                args.Completed += new EventHandler<SocketAsyncEventArgs>(OnConnect);
                socket.ConnectAsync(args);
            }
            catch (Exception exp)
            {
                updateDetailLogLabel("\n" + exp.Message);
            }
        }

        private void OnConnect(object sender, SocketAsyncEventArgs e)
        {
            if (socket.Connected)
            {
                StartReceiver();
                StartReader();
            }
            else
            {
                updateDetailLogLabel("\nCould not connect.");
            }
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (aviWriter == null || aviWriter.IsClosed)
                {
                    int frameRate = Int32.Parse(FrameRateTextBox.Text);

                    SaveFileDialog d = new SaveFileDialog();
                    d.Filter = "AVI | *.avi";
                    d.DefaultExt = "avi";
                    bool? k = d.ShowDialog();
                    if (!k.HasValue || !k.Value)
                        return;

                    aviWriter = new AviWriterJPGCompressed(d.OpenFile(), FRAME_WIDTH, FRAME_HEIGHT, frameRate);
                }

                RecordButton.IsEnabled = false;
                PauseButton.IsEnabled = true;
                StopButton.IsEnabled = true;
                FrameRateTextBox.IsEnabled = false;

                /*
                 * Start capturing screen.
                 */
                SendData("run");
            }
            catch (Exception exp)
            {
                updateDetailLogLabel("\n" + exp.Message);
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RecordButton.IsEnabled = true;
                PauseButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                SendData("stop");
            }
            catch (Exception exp)
            {
                updateDetailLogLabel("\n" + exp.Message);
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RecordButton.IsEnabled = true;
                PauseButton.IsEnabled = false;
                StopButton.IsEnabled = false;
                FrameRateTextBox.IsEnabled = true;
                
                SendData("stop");

                lock (aviWriter)
                {
                    aviWriter.Close();
                }
            }
            catch (Exception exp)
            {
                updateDetailLogLabel("\n" + exp.Message);
            }
        }

        public void StartReceiver()
        {
            try
            {
                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.SetBuffer(new byte[512], 0, 512);
                args.Completed += new EventHandler<SocketAsyncEventArgs>(onReceive);

                socket.ReceiveAsync(args);
            }
            catch (Exception exp)
            {
                updateDetailLogLabel("\n" + exp.Message);
            }
        }

        public void onReceive(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                lock (bytes)
                {
                    for (int i = 0; i < e.BytesTransferred; i++)
                        bytes.Add(e.Buffer[i]);
                }
            }
            catch (Exception exp)
            {
                updateDetailLogLabel("\n" + exp.Message);
            }
            finally
            {
                /*
                 * Start waiting for TCP data again.
                 */
                StartReceiver();
            }
        }

        private void StartReader()
        {
            readerThread = new Thread(new ThreadStart(Read));
 	        readerThread.Start();
        }

        private void Read()
        {
            List<byte> lt = new List<byte>();

            try
            {
                while (true)
                {
                    int bytesCount = 0;
                    lock (bytes)
                    {
                        bytesCount = bytes.Count;
                    }

                    if (bytesCount < 4)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    byte[] d = new byte[bytesCount];
                    lock (bytes)
                    {
                        bytes.CopyTo(0, d, 0, bytesCount);
                        bytes.RemoveRange(0, bytesCount);
                    }

                    for (int i = 0; i < bytesCount; i++)
                    {
                        lt.Add(d[i]);
                        if (lt.Count < 4)
                            continue;

                        byte[] b = lt.GetRange(lt.Count - 4, 4).ToArray();
                        if (isEqual(b, STRING_SEPARATOR_CODE))
                        {
                            byte[] c = lt.GetRange(0, 4).ToArray();
                            lt.RemoveRange(0, 4);

                            lt.RemoveRange(lt.Count - 4, 4);
                            byte[] t = lt.ToArray();

                            ReadCompletedTransmission(c, t);
                            lt.Clear();
                        }
                    }
                }
            }
            catch (Exception exp)
            {
            }
        }

        private bool isEqual(byte[] b, byte[] d)
        {
            for (int i = 0; i < b.Length && i < d.Length; i++)
            {
                if (b[i] != d[i])
                    return false;
            }

            return true;
        }

        private void ReadCompletedTransmission(byte[] c, byte[] t)
        {
            try
            {
                if (isEqual(c, DIMENSION_CODE))
                {
                    String msg = GetString(t);

                    /*
                     * Device width and height.
                     */
                    string[] dim = msg.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    FRAME_WIDTH = Int32.Parse(dim[0]);
                    FRAME_HEIGHT = Int32.Parse(dim[1]);

                    /*
                     * Enable record button.
                     */
                    EnableRecordButton(true);

                    /*
                     * Update log. 
                     */
                    updateDetailLogLabel("\nDevice found.");
                    updateDetailLogLabel("\nDimensions : (" + msg + ")");
                }
                else if (isEqual(c, ERROR_CODE))
                {
                    updateDetailLogLabel("\nError : " + GetString(t));
                }
                else if (isEqual(c, FRAME_CODE))
                {
                    AddFrame(t);
                }
            }
            catch (Exception e)
            {
                updateDetailLogLabel("\n" + e.Message);
            }
        }

        private String GetString(byte[] t)
        {
            char[] chars = new char[t.Length];
            System.Text.UTF8Encoding.UTF8.GetDecoder().GetChars(t, 0, t.Length, chars, 0);
            return new String(chars);
        }

        private void AddFrame(byte[] frame)
        {
            try
            {
                MemoryStream stream = new MemoryStream(frame);
                lock (aviWriter)
                {
                    if(!aviWriter.IsClosed)
                        aviWriter.AddFrame(frame);
                }

                /*
                 * Display current frame.
                 */
                Dispatcher.BeginInvoke(new Action<System.Windows.Controls.Image, Stream>(updateImage), imageViewer, stream);
            }
            catch (Exception e)
            {
                updateDetailLogLabel("\n" + e.Message);
            }
        }

        private void updateImage(System.Windows.Controls.Image image, Stream stream)
        {
            try
            {
                /*
                 * Display frame now.
                 */
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.SetSource(stream);
                image.Source = bitmapImage;
            }
            catch (Exception e)
            {
                updateDetailLogLabel("\n" + e.Message);
            }
        }

        private void updateDetailLogLabel(string data)
        {
            Dispatcher.BeginInvoke(new Action<TextBox, string>(updateLabel), labelDetailLog, data);
        }

        private void updateLabel(TextBox label, string data)
        {
            label.Text += data;
        }

        private void EnableRecordButton(bool value)
        {
            Dispatcher.BeginInvoke(new Action<Button, bool>(EnableButton), RecordButton, value);
        }

        private void EnableButton(Button button, bool value)
        {
            button.IsEnabled = value;
        }

        private void SendData(string data)
        {
            try
            {
                Byte[] bytes = Encoding.UTF8.GetBytes(data + "\r");

                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.SetBuffer(bytes, 0, bytes.Length);
                args.UserToken = socket;
                args.RemoteEndPoint = remoteEndPoint;
                socket.SendAsync(args);
            }
            catch (Exception e)
            {
                updateDetailLogLabel("\n" + e.Message);
            }
        }
    }
}
