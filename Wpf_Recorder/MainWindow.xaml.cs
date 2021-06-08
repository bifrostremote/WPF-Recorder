using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Wpf_Recorder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Thread record;
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnWindowclose(object sender, EventArgs e)
        {
            Environment.Exit(Environment.ExitCode); // Prevent memory leak
                                                    // System.Windows.Application.Current.Shutdown(); // Not sure if needed
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr onj);

        #region Compare frame code
        byte[] previousFrame;
        public byte[] CompareFrame(byte[] newFrame)
        {
            byte[] returnFrame = new byte[newFrame.Length];

            if (previousFrame != null)
            {
                Parallel.For(0, newFrame.Length, index =>
                //Parallel.ForEach(newFrame, (frame, fx, index) =>
                {
                    if (newFrame[index] == 0)
                        newFrame[index] = 1;

                    if (previousFrame[index] == newFrame[index])
                        returnFrame[index] = 0;
                    else
                        returnFrame[index] = newFrame[index];
                });
            }
            else
                returnFrame = newFrame;

            previousFrame = newFrame;

            return returnFrame;
        }
        #endregion

        private static byte[] Compress(byte[] input)
        {
            using (MemoryStream compressStream = new MemoryStream())
            using (DeflateStream compressor = new DeflateStream(compressStream, CompressionMode.Compress))
            {
                //input.CopyTo(compressor);
                compressor.Write(input, 0, input.Length);
                compressor.Close();
                return compressStream.ToArray();
            }
        }

        public int GetPercentage()
        {
            int progress = 0;
            Application.Current.Dispatcher.Invoke(
                DispatcherPriority.Normal,
                (ThreadStart)delegate { progress = (int)Percentage_Sleep.Value; }
            );
            // Default to 10000 if value is 0;
            if (progress == 0)
                progress = 10000;
            return progress;
        }

        Random rnd = new Random();
        #region Send data
        public void SendByteStrean(byte[][] chunks, int dataLen, int imageWidth, int imageHeight, string ip, int port)
        {
            // Create the endpoint the the udp connection will use.
            IPEndPoint ep = new IPEndPoint(IPAddress.Parse(ip), port);

            // Create a new socket.
            Socket sending_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // Split data into 1000 item chunks.
            //byte[][] chunks = data
            //        .Select((s, i) => new { Value = s, Index = i })
            //        .GroupBy(x => x.Index / 1000)
            //        .Select(grp => grp.Select(x => x.Value).ToArray())
            //        .ToArray();

            // Generate id for package.
            byte[] id = new byte[2];
            rnd.NextBytes(id);
            // Send header to prepare for new message.
            byte[] header = new byte[]
            {
                101, // Start byte

                id[0], // ID_1
                id[1], // ID_2

                Convert.ToByte(chunks.Length % 255), // List count single
                Convert.ToByte((int)(chunks.Length / 255)), // List count tenths
                
                Convert.ToByte(dataLen % 255), // Total length single
                Convert.ToByte((int)((dataLen / 255) % 255)), // Total length tenths
                Convert.ToByte((int)((dataLen / 255) / 255)), // Total length tenths

                Convert.ToByte(imageWidth % 255), // Canvas Width single
                Convert.ToByte((int)(imageWidth / 255)), // Width tenths

                Convert.ToByte(imageHeight % 255), // Canvas Height single
                Convert.ToByte((int)(imageHeight / 255)) // Height tenths
            };

            // Send data to the endpoint using the socket connection.
            sending_socket.SendTo(header, ep);
            Thread.Sleep(1);

            int percentage = GetPercentage();
            for (int i = 0; i < chunks.Length; i++)
            {
                // Break if all chunks are empty.
                if (chunks[i] == null || chunks[i].All(x => x == 0))
                    continue;
                //var id = GetRandomHexNumber(4);
                // Create a custom data packet.
                header = new byte[]
                {
                    0, // Start byte
                    id[0], // ID_1
                    id[1], // ID_2
                    Convert.ToByte(i%255), // Index single
                    Convert.ToByte((int)(i/255)), // Index tenths
                };

                byte[] package = new byte[header.Length + chunks[i].Length];
                header.CopyTo(package, 0);
                chunks[i].CopyTo(package, header.Length);

                // Send data to the endpoint using the socket connection.
                sending_socket.SendTo(package, ep);
                if (i % percentage == 0)
                    Thread.Sleep(1);
            }
        }
        #endregion

        public static byte[] ImageToByte(Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
        }

        public bool loop;

        public void RecordScreen()
        {
            while (loop)
            {
                Bitmap bitmap;

                bitmap = new Bitmap((int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
                }

                // Handle code.
                byte[] bitByte = ImageToByte(bitmap);
                //bitByte = CompareFrame(bitByte);

                int sendSize = 500;
                #region Split function.
                int size = bitByte.Length / sendSize;
                byte[][] chunks = new byte[size + 1][];
                //for (int i = 0; i < size; i++)
                Parallel.For(0, size, (i) =>
                {
                    byte[] tmpArr = new byte[sendSize];
                    Array.Copy(bitByte, i * sendSize, tmpArr, 0, sendSize);
                    chunks[i] = tmpArr;
                });
                // Take last item.
                byte[] tmpArr2 = new byte[bitByte.Length % sendSize];
                Array.Copy(bitByte, size * sendSize, tmpArr2, 0, bitByte.Length % sendSize);
                chunks[size] = tmpArr2;
                #endregion

                #region Compress parts
                //byte[][] compressedChunks = new byte[chunks.Length][];
                //for (int i = 0; i < chunks.Length; i++)
                //{
                //    if (!chunks[i].All(x => x == 0))
                //        compressedChunks[i] = Compress(chunks[i]);
                //}
                #endregion
                // Send data
                //SendByteStrean(myStream.GetBuffer(), ImageSize, "127.0.0.1", 11000); //"10.142.105.45"
                SendByteStrean(chunks, bitByte.Length, bitmap.Width, bitmap.Height, "127.0.0.1", 11000); //"10.142.112.247"


                UpdateImage(bitmap);


                // Display crap.
                //IntPtr handle = IntPtr.Zero;
                //try
                //{
                //    handle = bitmap.GetHbitmap();
                //    ImageControl.Source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty,
                //        BitmapSizeOptions.FromEmptyOptions());

                //    //bitmap.Save("C:\\1.jpg"); //saving
                //}
                //catch (Exception)
                //{

                //}

                //finally
                //{
                //    DeleteObject(handle);
                //}
                //Thread.Sleep(10);
            }
        }

        private void Start_Streaming(object sender, RoutedEventArgs e)
        {
            loop = true;
            record = new Thread(RecordScreen);
            record.IsBackground = true;
            record.Start();
        }

        private void Stop_Streaming(object sender, RoutedEventArgs e)
        {
            loop = false;
        }

        private void UpdateImage(Bitmap bitmap)
        {
            Dispatcher.Invoke(() => {
                //Matrix m = PresentationSource.FromVisual(Application.Current.MainWindow).CompositionTarget.TransformToDevice;
                //double dx = m.M11;
                //double dy = m.M22;

                //WriteableBitmap writeableBitmap = new WriteableBitmap(imageWidth, imageHeight, dx, dy, PixelFormats.Bgr32, null);
                //WriteableBitmap wb = writeableBitmap;

                //using (Stream stream = wb.PixelBuffer.AsStream())
                //{
                //    //write to bitmap
                //    stream.WriteAsync(bitmapImage, 0, bitmapImage.Length);
                //}

                //Bitmap bitmap = ByteToImage(bitmapImage);
                //var bitmap = (BitmapSource)new ImageSourceConverter().ConvertFrom(bitmapImage);
                //ImageContainer.Source = bitmap;
                //ImageContainer.Width = imageWidth;
                //ImageContainer.Height = imageHeight;

                // Display crap.
                IntPtr handle = IntPtr.Zero;
                try
                {
                    handle = bitmap.GetHbitmap();
                    ImageControl.Source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    //bitmap.Save("C:\\1.jpg"); //saving
                }
                catch (Exception)
                {

                }

                finally
                {
                    DeleteObject(handle);
                }
                //using (var ms = new MemoryStream(bitmapImage))
                //{
                //    var image = new BitmapImage();
                //    image.BeginInit();
                //    image.CacheOption = BitmapCacheOption.OnLoad; // here
                //    image.StreamSource = ms;
                //    image.EndInit();

                //}
            });
        }
    }
}
