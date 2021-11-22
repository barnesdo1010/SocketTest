using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocketTest
{
    public partial class Form1 : Form
    {
        private DateTime _LastLogMessage = DateTime.Now;
        private Socket _s;
        public Socket s
        {
            get { return _s; }
            set { _s = value; }
        }
        private List<Socket> _Error = new List<Socket>();
        public IPEndPoint EndPoint;

        public bool connected;

        StringBuilder _EventBuffer = new StringBuilder();
        string ReceivedData = null;
        public string RecData, Temp, Oxygen, Carbon, Milivolt = null;
        public string currentHeat, currentHeatID, station = null;


        public Commands CommandForThread;
        public string IPAddress;
        public int Port;
        public string MessageToLog;
        public Form1()
        {
            InitializeComponent();
        }

        public enum Commands
        {
            None,
            CreateSocket,
            Connect,
            Disconnect,
            Receive,
            Parse,
            SendData,
            LogFromThread
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            
        }

        public void ManualSocketControl()
        {
            while (true)
            {
                if (CommandForThread != Commands.None)
                {
                    switch (CommandForThread)
                    {
                        case Commands.CreateSocket:
                            CreateSocket(IPAddress, Port);
                            break;
                        case Commands.Connect:
                            Connect();
                            break;
                        case Commands.Disconnect:
                            Disconnect();
                            break;
                        case Commands.Receive:
                            Receive();
                            break;
                        case Commands.Parse:
                            Parse();
                            break;
                        case Commands.SendData:
                            SendData();
                            break;
                        case Commands.LogFromThread:
                            Log(MessageToLog);
                            break;

                    }
                    CommandForThread = Commands.None;
                }
            }
        }

        private void AutoSocketControl()
        {
            while (true)
            {
                if (!connected)
                {
                    CreateSocket("10.112.1.1", 12345);
                    Connect();
                }
                else
                {
                    Receive();
                }
            }
            Disconnect();
        }

        public void LogMessage(string message)
        {
            // List View
            ListViewItem tmp = new ListViewItem(DateTime.Now.ToString());
            tmp.SubItems.Add(message);
            listView1.Invoke((MethodInvoker)delegate ()
            {
                listView1.Items.Insert(0, tmp);
                if (listView1.Items.Count > 50)
                    listView1.Items.RemoveAt(50);
            });
        }

        public void CreateSocket(string ip, int port)
        {
            try
            {
                s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                s.NoDelay = true;
                s.ReceiveTimeout = 5000;
                s.SendTimeout = 5000;
                EnableKeepalive(s);
                EndPoint = new IPEndPoint(System.Net.IPAddress.Parse(ip), port);
                LogMessage("Socket Created");
            }
            catch (Exception ex)
            {
                LogMessage(ex.ToString());
                Disconnect();
            }
        }
        public void Connect()
        {
            LogMessage("connecting...");
            try
            {
                s.Connect(EndPoint);
                if (s.Connected)
                {
                    LogMessage("Connected!");
                    connected = true;
                }
                else
                {
                    LogMessage("Not connected. Closing socket...");
                    Disconnect();
                }
            }
            catch (Exception ex)
            {
                LogMessage(ex.ToString());
                Disconnect();
            }
        }
        public void Disconnect()
        {
            LogMessage("disconnecting");
            if (null != s)
            {
                try
                {
                    s.Shutdown(SocketShutdown.Both);
                }
                catch (Exception ex)
                {
                    LogMessage($"Socket Client Disconnect Error: {ex.ToString()}");
                }
                finally
                {
                    connected = false;
                    s.Close();
                    s = null;
                    Dispose();
                }
            }

        }

        public void Receive()
        {
            byte[] buffer = new byte[256];
            try
            {
                if (!HasErrored())
                {
                    if (s.Poll(1, SelectMode.SelectRead))
                    {
                        int bytes = s.Receive(buffer);
                        _EventBuffer.Append(Encoding.ASCII.GetString(buffer, 0, bytes));
                        if (0 == bytes)
                        {
                            LogMessage($"message string empty.");
                        }
                        else
                        {
                            ReceivedData = _EventBuffer.ToString();
                            LogMessage($"Received Data:  {ReceivedData}");
                        }
                    }
                }
                else
                {
                    LogMessage($"socket connection has errored!!");
                    connected = false;
                    Connect();
                    Receive();
                }
            }
            catch (Exception ex)
            {
                LogMessage(ex.ToString());
                connected = false;
                Connect();
            }
        }

        public bool HasErrored()
        {
            bool retVal = false;
            if (connected == true)
            {
                _Error.Add(s);
                Socket.Select(null, null, _Error, 500);
                if (_Error.Contains(s))
                {
                    retVal = true;
                    connected = false;
                    s.Close();
                    s = null;
                }
                else
                {
                    if (s.Poll(10, SelectMode.SelectRead) && s.Available == 0)
                    {
                        retVal = true;
                        connected = false;
                        s.Close();
                        s = null;
                    }
                }
                _Error.Clear();
            }
            return retVal;
        }
        private void EnableKeepalive(Socket s)
        {
            // Get the size of the uint to use to back the byte array
            int size = Marshal.SizeOf((uint)0);

            // Create the byte array
            byte[] keepAlive = new byte[size * 3];

            // Pack the byte array:
            // Turn keepalive on
            Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAlive, 0, size);
            // Set amount of time without activity before sending a keepalive to 5 seconds
            Buffer.BlockCopy(BitConverter.GetBytes((uint)5000), 0, keepAlive, size, size);
            // Set keepalive interval to 5 seconds
            Buffer.BlockCopy(BitConverter.GetBytes((uint)5000), 0, keepAlive, size * 2, size);

            // Set the keep-alive settings on the underlying Socket
            s.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);
        }

        public void Parse()
        {
            LogMessage("parsing the received string from device...");
            try
            {
                if (ReceivedData.Length > 1)
                {
                    RecData = ReceivedData;
                    string word = ReceivedData;
                    int TempStartIndex = word.IndexOf('$');
                    int OxyStartIndex = word.IndexOf('#');
                    int MiliVolt = word.IndexOf('%');
                    int CarbonStartIndex = word.IndexOf('&');
                    int end = word.IndexOf('/');

                    Temp = word.Substring(TempStartIndex + 1, (MiliVolt - 1) - TempStartIndex).Trim();
                    Oxygen = word.Substring(OxyStartIndex + 1, (TempStartIndex - 1) - OxyStartIndex).Trim();
                    Milivolt = word.Substring(MiliVolt + 1, (CarbonStartIndex - 1) - MiliVolt).Trim();
                    Carbon = word.Substring(CarbonStartIndex + 1, (end - 2) - CarbonStartIndex).Trim();

                    LogMessage($"Heat: {currentHeat} Temp: {Temp}°F  Oxygen: {Oxygen} Carbon: {Carbon}");
                }

            }
            catch (Exception ex) { LogMessage($"Could not parse data. {ex}"); }
        }
        public void SendData()
        {
            LogMessage("sending the parsed data to database...");
        }
    }
}
