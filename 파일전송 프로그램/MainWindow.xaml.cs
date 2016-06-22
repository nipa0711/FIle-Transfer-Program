using FUP;
using Microsoft.Win32;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;

namespace 파일전송_프로그램
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        private string folderPath;
        private string ipPath;
        private const int connectedTime = 30000;
        private int port;
        private bool isServerAlive = true;
        iniUtil ini;

        public void setFolderPath(string path)
        {
            this.folderPath = path;
            fileSaveLocBox.Text = this.folderPath;
        }

        public string getFolderPath()
        {
            return this.folderPath;
        }

        public string getipPath()
        {
            return this.ipPath;
        }

        public int getPort()
        {
            return this.port;
        }

        public void setPort(int port)
        {
            this.port = port;
            portNum.Text = this.port.ToString();
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        public void killServer()
        {
            this.isServerAlive = false;
        }

        private void addFileBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();

            openFileDialog.InitialDirectory = "c:\\"; // 기본경로
            openFileDialog.Filter = "모든파일 (*.*)|*.*"; // 파일 유형 선택
            openFileDialog.RestoreDirectory = true;
            openFileDialog.Multiselect = true; // 중복선택

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (String file in openFileDialog.FileNames) // 여러개
                {
                    //MessageBox.Show("선택한 파일 : " + file); // 파일 입력 확인
                    showFileListBox.Items.Add(file);
                }
            }
        }

        private void removeFileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (showFileListBox.SelectedItem != null)
            {
                showFileListBox.Items.RemoveAt(showFileListBox.SelectedIndex); // 선택된 것 삭제
            }
        }

        private void transferFileBtn_Click(object sender, RoutedEventArgs e)
        {
            IPAddress get_ip;
            bool valid = IPAddress.TryParse(ipAddr.Text, out get_ip); // ip 유효성 검사

            if (valid == false)
            {
                MessageBox.Show("IP주소가 잘못되었습니다."); // 경고
            }
            else
            {
                if (showFileListBox.Items.Count == 0)
                {
                    UpdateLogBox("전송할 파일이 없습니다.");
                    return;
                }

                this.ipPath = ipAddr.Text;
                setPort(Int32.Parse(portNum.Text));
                Thread fileUpload = new Thread(uploader);
                fileUpload.IsBackground = true;
                fileUpload.Start();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            String curSaveLoc = AppDomain.CurrentDomain.BaseDirectory;
            setFolderPath(curSaveLoc);
            setPort(5425);
            serverManagement(true);

            // 내 외부 IP 갖고오기
            WebClient wc = new WebClient();
            wc.Encoding = System.Text.Encoding.Default;
            string html = wc.DownloadString("http://ipip.kr");
            Regex regex = new Regex(@"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}"); // 정규식
            Match m = regex.Match(html);
            myGlobalIP.Content = m.ToString();

            // 내 내부 IP 갖고오기
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            string localIP = string.Empty;
            for (int i = 0; i < host.AddressList.Length; i++)
            {
                if (host.AddressList[i].AddressFamily == AddressFamily.InterNetwork)
                {
                    localIP = host.AddressList[i].ToString();
                }
            }
            myLocalIP.Content = localIP;

            // 설정 저장파일 만들기

            string path = getFolderPath();  //프로그램 실행되고 있는데 path 가져오기
            string fileName = @"\config.ini";  //파일명
            string filePath = path + fileName;   //ini 파일 경로
            ini = new iniUtil(filePath);

            FileInfo fi = new FileInfo(filePath);
            if (fi.Exists)
            {
                loadSetting();
            }
        }

        private void setLocationBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog cd = new OpenFileDialog();
            cd.FileName = "Change Directory";
            cd.CheckFileExists = false;
            cd.CheckPathExists = true;
            cd.ValidateNames = false;

            if (cd.ShowDialog() == true)
            {
                string fileName = cd.SafeFileName;
                string fileFullName = cd.FileName;
                string filePath = fileFullName.Replace(fileName, "");

                setFolderPath(filePath);
            }
            saveSetting();
        }

        private void UpdateLogBox(string data)
        {
            // 해당 쓰레드가 UI쓰레드인가?
            if (logBox.Dispatcher.CheckAccess())
            {
                //UI 쓰레드인 경우
                logBox.AppendText(data + Environment.NewLine);
                logBox.ScrollToLine(logBox.LineCount - 1); // 로그창 스크롤 아래로
            }
            else
            {
                // 작업쓰레드인 경우
                logBox.Dispatcher.BeginInvoke((Action)(() => { logBox.AppendText(data + Environment.NewLine); logBox.ScrollToLine(logBox.LineCount - 1); }));
            }
        }

        private void startServer()
        {
            uint msgId = 0;

            int bindPort = getPort();

            string clientIP = null;
            TcpListener server = null;
            try
            {
                IPEndPoint localAddress = new IPEndPoint(IPAddress.Any, bindPort);

                server = new TcpListener(localAddress);
                server.Start();

                UpdateLogBox("파일 업로드 서버 시작... ");
                UpdateLogBox("현재 포트번호 : " + bindPort);

                while (this.isServerAlive)
                {
                    TcpClient client = server.AcceptTcpClient();
                    string connectedIP = ((IPEndPoint)client.Client.RemoteEndPoint).ToString();

                    UpdateLogBox("클라이언트 접속 : " + connectedIP);

                    NetworkStream stream = client.GetStream();

                    Message reqMsg = MessageUtil.Receive(stream);

                    if (reqMsg.Header.MSGTYPE != CONSTANTS.REQ_FILE_SEND)
                    {
                        stream.Close();
                        client.Close();
                        continue;
                    }

                    BodyRequest reqBody = (BodyRequest)reqMsg.Body;

                    Message rspMsg = new Message();
                    rspMsg.Body = new BodyResponse()
                    {
                        MSGID = reqMsg.Header.MSGID,
                        RESPONSE = CONSTANTS.ACCEPTED
                    };
                    rspMsg.Header = new Header()
                    {
                        MSGID = msgId++,
                        MSGTYPE = CONSTANTS.REP_FILE_SEND,
                        BODYLEN = (uint)rspMsg.Body.GetSize(),
                        FRAGMENTED = CONSTANTS.NOT_FRAGMENTED,
                        LASTMSG = CONSTANTS.LASTMSG,
                        SEQ = 0
                    };

                    if (clientIP == null) // 같은 IP에서 보낸 경우 한번만 승낙하면 되게
                    {
                        MessageBoxResult result = MessageBox.Show(connectedIP.Substring(0, 12) + "으로 부터 파일 업로드 요청이 왔습니다. 수락하시겠습니까?", "파일 업로드 요청", MessageBoxButton.OKCancel);

                        if (result == MessageBoxResult.OK)
                        {
                            UpdateLogBox("파일 전송을 시작합니다... ");
                            MessageUtil.Send(stream, rspMsg);
                            clientIP = connectedIP.Substring(0, 12);
                        }
                        else
                        {
                            rspMsg.Body = new BodyResponse()
                            {
                                MSGID = reqMsg.Header.MSGID,
                                RESPONSE = CONSTANTS.DENIED
                            };
                            MessageUtil.Send(stream, rspMsg);
                            stream.Close();
                            client.Close();
                            UpdateLogBox("파일 전송을 거절했습니다.");

                            continue;
                        }
                    }
                    else
                    {
                        UpdateLogBox("파일 전송을 시작합니다... ");
                        MessageUtil.Send(stream, rspMsg);
                    }


                    long fileSize = reqBody.FILESIZE;
                    string fileName = Encoding.Default.GetString(reqBody.FILENAME);

                    String curSaveLoc = getFolderPath();
                    curSaveLoc = curSaveLoc + @"\download";
                    DirectoryInfo di = new DirectoryInfo(curSaveLoc);

                    if (di.Exists == false)
                    {
                        di.Create(); // download 폴더 생성
                    }

                    FileStream file =
                       new FileStream(curSaveLoc + "\\" + fileName, FileMode.Create);

                    uint? dataMsgId = null;
                    int prevSeq = 0;
                    while ((reqMsg = MessageUtil.Receive(stream)) != null)
                    {
                        if (reqMsg.Header.MSGTYPE != CONSTANTS.FILE_SEND_DATA)
                            break;

                        if (dataMsgId == null)
                            dataMsgId = reqMsg.Header.MSGID;
                        else
                        {
                            if (dataMsgId != reqMsg.Header.MSGID)
                                break;
                        }

                        if (prevSeq++ != reqMsg.Header.SEQ) // 메세지 순서가 어긋나면 전송 중단
                        {
                            UpdateLogBox("" + prevSeq + reqMsg.Header.SEQ);
                            break;
                        }

                        file.Write(reqMsg.Body.GetBytes(), 0, reqMsg.Body.GetSize()); // 전송받은 스트림을 생성한 파일에 기록

                        if (reqMsg.Header.LASTMSG == CONSTANTS.LASTMSG) // 마지막 메세지라면 반복문 종료
                            break;
                    }

                    long recvFileSize = file.Length;
                    file.Close();

                    UpdateLogBox("수신 파일 크기 : " + recvFileSize + "bytes");

                    Message rstMsg = new Message();
                    rstMsg.Body = new BodyResult()
                    {
                        MSGID = reqMsg.Header.MSGID,
                        RESULT = CONSTANTS.SUCCESS
                    };
                    rstMsg.Header = new Header()
                    {
                        MSGID = msgId++,
                        MSGTYPE = CONSTANTS.FILE_SEND_RES,
                        BODYLEN = (uint)rstMsg.Body.GetSize(),
                        FRAGMENTED = CONSTANTS.NOT_FRAGMENTED,
                        LASTMSG = CONSTANTS.LASTMSG,
                        SEQ = 0
                    };

                    if (fileSize == recvFileSize)
                        MessageUtil.Send(stream, rstMsg);
                    else
                    {
                        rstMsg.Body = new BodyResult()
                        {
                            MSGID = reqMsg.Header.MSGID,
                            RESULT = CONSTANTS.FAIL
                        };

                        MessageUtil.Send(stream, rstMsg);
                    }
                    UpdateLogBox("파일 전송을 마쳤습니다.");

                    stream.Close();
                    client.Close();
                }
            }
            catch (SocketException e)
            {
                UpdateLogBox("" + e);
            }
            finally
            {
                server.Stop();
                this.isServerAlive = true;
                serverManagement(true);
            }
        }

        private void uploader()
        {
            for (int i = 0; i < showFileListBox.Items.Count; i++)
            {
                String filePath = showFileListBox.Items[i].ToString();
                FileSender(filePath);
            }
        }

        private void FileSender(string filepath)
        {
            const int CHUNK_SIZE = 4096;
            string serverIp = getipPath();
            int serverPort = getPort();

            try
            {
                IPEndPoint clientAddress = new IPEndPoint(IPAddress.Any, 0);
                IPEndPoint serverAddress =
                    new IPEndPoint(IPAddress.Parse(serverIp), serverPort);

                UpdateLogBox("클라이언트 : " + clientAddress.ToString() + ", 서버 : " + serverAddress.ToString());

                uint msgId = 0;

                Message reqMsg = new Message();
                reqMsg.Body = new BodyRequest()
                {
                    FILESIZE = new FileInfo(filepath).Length,
                    FILENAME = System.Text.Encoding.Default.GetBytes(System.IO.Path.GetFileName(filepath))
                };
                reqMsg.Header = new Header()
                {
                    MSGID = msgId++,
                    MSGTYPE = CONSTANTS.REQ_FILE_SEND,
                    BODYLEN = (uint)reqMsg.Body.GetSize(),
                    FRAGMENTED = CONSTANTS.NOT_FRAGMENTED,
                    LASTMSG = CONSTANTS.LASTMSG,
                    SEQ = 0
                };

                TcpClient client = new TcpClient(clientAddress);
                client.Connect(serverAddress);

                NetworkStream stream = client.GetStream();

                MessageUtil.Send(stream, reqMsg);

                Message rspMsg = MessageUtil.Receive(stream);

                if (rspMsg.Header.MSGTYPE != CONSTANTS.REP_FILE_SEND)
                {
                    UpdateLogBox("정상적인 서버 응답이 아닙니다." + rspMsg.Header.MSGTYPE);
                    return;
                }

                if (((BodyResponse)rspMsg.Body).RESPONSE == CONSTANTS.DENIED)
                {
                    UpdateLogBox("서버에서 파일 전송을 거부했습니다.");
                    return;
                }

                using (Stream fileStream = new FileStream(filepath, FileMode.Open))
                {
                    byte[] rbytes = new byte[CHUNK_SIZE];

                    long readValue = BitConverter.ToInt64(rbytes, 0);

                    int totalRead = 0;
                    ushort msgSeq = 0;
                    byte fragmented =
                        (fileStream.Length < CHUNK_SIZE) ?
                        CONSTANTS.NOT_FRAGMENTED : CONSTANTS.FRAGMENTED;

                    while (totalRead < fileStream.Length)
                    {
                        int read = fileStream.Read(rbytes, 0, CHUNK_SIZE);
                        totalRead += read;
                        Message fileMsg = new Message();

                        byte[] sendBytes = new byte[read];
                        Array.Copy(rbytes, 0, sendBytes, 0, read);

                        fileMsg.Body = new BodyData(sendBytes);
                        fileMsg.Header = new Header()
                        {
                            MSGID = msgId,
                            MSGTYPE = CONSTANTS.FILE_SEND_DATA,
                            BODYLEN = (uint)fileMsg.Body.GetSize(),
                            FRAGMENTED = fragmented,
                            LASTMSG = (totalRead < fileStream.Length) ?
                                      CONSTANTS.NOT_LASTMSG :
                                      CONSTANTS.LASTMSG,
                            SEQ = msgSeq++
                        };

                        MessageUtil.Send(stream, fileMsg);
                    }

                    Message rstMsg = MessageUtil.Receive(stream);

                    BodyResult result = ((BodyResult)rstMsg.Body);
                    UpdateLogBox("파일 전송 성공 : " + (result.RESULT == CONSTANTS.SUCCESS));
                }

                stream.Close();
                client.Close();
            }
            catch (SocketException e)
            {
                UpdateLogBox("" + e);
            }

            //UpdateLogBox("파일 전송을 마쳤습니다.");
        }

        private void eraseLogBtn_Click(object sender, RoutedEventArgs e)
        {
            logBox.Text = "로그를 삭제했습니다\n-----------------------------\n";
            UpdateLogBox("파일 업로드 서버 시작... ");
        }

        private void portNum_LostFocus(object sender, RoutedEventArgs e)
        {
            setPort(Int32.Parse(portNum.Text));

            UpdateLogBox("포트번호가 변경되었습니다.");
            UpdateLogBox("새로운 포트번호 : " + getPort());
            UpdateLogBox("서버를 재시작 합니다.");
            serverManagement(false);
        }

        private void serverManagement(bool serverActivate)
        {
            Thread th_server;
            th_server = new Thread(startServer);

            if (serverActivate == true)
            {
                UpdateLogBox("서버를 시작합니다.");

                th_server.IsBackground = true;
                th_server.Start();
            }
            else
            {
                th_server.Abort();
                killServer();
            }
        }

        private void checkBox_Checked(object sender, RoutedEventArgs e)
        {
            RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(
                                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            registryKey.SetValue("FileTransferProgram", System.Reflection.Assembly.GetExecutingAssembly().Location);
            UpdateLogBox("시작 프로그램에 등록되었습니다.");

        }

        private void autoStart_Unchecked(object sender, RoutedEventArgs e)
        {
            RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(
                                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            registryKey.DeleteValue("FileTransferProgram", false);
            UpdateLogBox("시작 프로그램에서 해제되었습니다.");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            saveSetting();
        }

        private void saveSetting()
        {
            ini.SetIniValue("Setting", "AutoStart", autoStart.IsChecked.ToString());
            ini.SetIniValue("Setting", "downLocation", getFolderPath());
        }

        private void loadSetting()
        {
            string isAutoStart = ini.GetIniValue("Setting", "AutoStart");
            autoStart.IsChecked = Convert.ToBoolean(isAutoStart);

            setFolderPath(ini.GetIniValue("Setting", "downLocation")); // 다운 폴더 불러오기
        }
    }
}
