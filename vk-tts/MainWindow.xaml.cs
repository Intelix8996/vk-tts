using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VkNet;
using VkNet.Enums.Filters;
using VkNet.Model;
using VkNet.Model.RequestParams;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using VkNet.Model.Attachments;
using VkNet.Enums.SafetyEnums;
using Newtonsoft.Json;

namespace vk_tts
{
    public partial class MainWindow : Window
    {
        DoubleAnimation elementShow, elementHide, backgroundBlur, backgroundUnBlur;

        VkApi api;
        VkNet.Exception.CaptchaNeededException _cex;

        string Login, Password;

        VkNet.Utils.VkCollection<User> Friends;

        long ChatID;

        List<MediaAttachment> MessageAttachment = new List<MediaAttachment>();

        LongPollServerResponse LongPollServer;

        public MainWindow()
        {
            InitializeComponent();
            
            elementShow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250));
            elementHide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            backgroundBlur = new DoubleAnimation(10, TimeSpan.FromMilliseconds(250));
            backgroundUnBlur = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));

            if (CheckPasswordChache() == null)
                ShowLoginField();
            else
            {
                string str = CheckPasswordChache();

                RC4 Decoder = new RC4(ASCIIEncoding.ASCII.GetBytes(CheckPasswordChache()));

                string rawLoginPassword = ASCIIEncoding.ASCII.GetString(Decoder.Decode(File.ReadAllBytes(CheckPasswordChache()), File.ReadAllBytes(CheckPasswordChache()).Length));

                Login = rawLoginPassword.Split(' ')[0];
                Password = rawLoginPassword.Split(' ')[1];

                LogIn(Login, Password);
            }
        }

        private void loginButton_Click(object sender, RoutedEventArgs e)
        {
            Login = loginInputBox.Text;
            Password = passwordInputBox.Password;

            HideLoginField();

            LogIn(Login, Password);
        }

        async void LogIn(string Login, string Password)
        {
            ShowLoadingScreen();

            api = new VkApi();

            try
            {
                await api.AuthorizeAsync(new ApiAuthParams
                {
                    ApplicationId = 6630347,
                    Login = Login,
                    Password = Password,
                    Settings = Settings.All
                });

                HideLoadingScreen();
            }
            catch (VkNet.Exception.CaptchaNeededException cEx)
            {
                HideLoadingScreen();

                ShowCapchaWindow();

                _cex = cEx;

                capchaImage.Source = new BitmapImage(new Uri(cEx.Img.AbsoluteUri));
            }

            if (savePasswordCheckBox.IsChecked == true)
            {
                string fileName = Environment.CurrentDirectory + "\\" + Convert.ToString(DateTimeOffset.UtcNow.ToUnixTimeSeconds()) + ".pwdsv";

                RC4 Encoder = new RC4(ASCIIEncoding.ASCII.GetBytes(fileName));

                File.WriteAllBytes(fileName, Encoder.Encode(ASCIIEncoding.ASCII.GetBytes(Login + " " + Password), ASCIIEncoding.ASCII.GetBytes(Login + " " + Password).Length));
            }

            ConnectToLongPollServer();
            RefreshChatList();
        }

        async void ConnectToLongPollServer()
        {
            LongPollServer = await api.Messages.GetLongPollServerAsync(true);

            LongPollServerLoop(LongPollServer.Ts);
        }

        async void LongPollServerLoop(ulong ts)
        {
            string baseUrl = "https://" + LongPollServer.Server + "?act=a_check&key=" + LongPollServer.Key + "&ts=" + ts + "&wait=25&mode=2&version=2 ";
            LongPollResponse Response;

            using (WebClient clinet = new WebClient())
            {
                string rawResponse = await clinet.DownloadStringTaskAsync(new Uri(baseUrl));

                if (rawResponse.Contains("title"))
                {
                    int startIdx = 0, endIdx = 0;

                    for (int i = 2; i < rawResponse.Length - 1; ++i)
                    {
                        if (rawResponse[i] == '{')
                            startIdx = i;

                        if (rawResponse[i] == '}')
                            endIdx = i;
                    }

                    rawResponse = rawResponse.Remove(startIdx - 1, endIdx - startIdx - 1);
                }

                Response = JsonConvert.DeserializeObject<LongPollResponse>(rawResponse);
            }

            HandleLongPollResponse(Response);
            LongPollServerLoop(Response.ts);
        }

        void HandleLongPollResponse(LongPollResponse Response)
        {

            foreach (string[] rawCommand in Response.updates)
            {
                switch (rawCommand[0])
                {
                    case "4":

                        if (ChatID != Convert.ToInt32(rawCommand[3]))
                            Chat.AppendText("Пользователь " + GetNameByID(Convert.ToInt64(rawCommand[3])) + " отправил сообщение: " + rawCommand[5]);
                        else if (GetNameByID(Convert.ToInt64(rawCommand[3])) != "Вы")
                            Chat.AppendText(rawCommand[5] + " ---- " + GetNameByID(Convert.ToInt64(rawCommand[3])));
                        break;
                }
            }
        }

        async void RefreshChatList()
        {
            ShowLoadingScreen();

            CommandBinding Binding = new CommandBinding(ApplicationCommands.CancelPrint);

            Binding.Executed += FriendChatChoose;

            CommandBindings.Add(Binding);

            Friends = await api.Friends.GetAsync(new FriendsGetParams {
                Fields = ProfileFields.FirstName | ProfileFields.LastName | ProfileFields.Online | ProfileFields.Photo100,
            });

            foreach (var Friend in Friends)
            {
                ChatList.Items.Add(new System.Windows.Controls.Button {
                    Content = Friend.FirstName + " " + Friend.LastName,
                    Command = ApplicationCommands.CancelPrint,
                    Opacity = 0,
                    Width = 320,
                });
            }

            foreach (System.Windows.Controls.Button Button in ChatList.Items)
            {
                Button.BeginAnimation(OpacityProperty, elementShow);
            }

            HideLoadingScreen();
        }

        private void FriendChatChoose(object sender, ExecutedRoutedEventArgs e)
        {
            System.Windows.Controls.Button CurrentButton = (System.Windows.Controls.Button)e.Source;

            foreach (var Friend in Friends)
            {
                if (Friend.FirstName + " " + Friend.LastName == CurrentButton.Content.ToString())
                    ChatID = Friend.Id;
            }

            RefreshChat();
        }

        async void RefreshChat()
        {
            ShowLoadingScreen();

            Chat.Text = "";

            var Messages = await api.Messages.GetHistoryAsync(new MessagesGetHistoryParams {
                UserId = ChatID,
                Count = 199,
                Reversed = false,
            });

            foreach (var message in Messages.Messages.Reverse())
            {
                if (message.Text != "")
                    Chat.AppendText("\n\n" + message.Text + " ---- " + GetNameByID((long)message.FromId));
                else
                    Chat.AppendText("\n\n Вложение ---- " + GetNameByID((long)message.FromId));
            }

            Chat.ScrollToEnd();

            HideLoadingScreen();
        }

        string GetNameByID(long id)
        {
            foreach (var Friend in Friends)
            {
                if (Friend.Id == id)
                    return Friend.FirstName + " " + Friend.LastName;
            }

            return "Вы";
        }

        string CheckPasswordChache()
        {
            string[] strs = Directory.GetFiles(Environment.CurrentDirectory);

            foreach (string str in strs)
            {
                if (str.Contains(".pwdsv"))
                {
                    return str;
                }
            }

            return null;
        }

        void RestartApp()
        {
            File.Delete(CheckPasswordChache());
            Process.Start("vk-tts.exe");
            Environment.Exit(0);
        }

        void ShowLoadingScreen()
        {
            loadingScreen.BeginAnimation(OpacityProperty, elementShow);

            BlurBackground();
        }

        void HideLoadingScreen()
        {
            loadingScreen.BeginAnimation(OpacityProperty, elementHide);

            UnBlurBackground();
        }

        void ShowLoginField()
        {
            loginButton.BeginAnimation(OpacityProperty, elementShow);
            loginInputBox.BeginAnimation(OpacityProperty, elementShow);
            passwordInputBox.BeginAnimation(OpacityProperty, elementShow);
            savePasswordCheckBox.BeginAnimation(OpacityProperty, elementShow);
            loginIcon.BeginAnimation(OpacityProperty, elementShow);
            passwordIcon.BeginAnimation(OpacityProperty, elementShow);

            BlurBackground();
        }

        void HideLoginField()
        {
            loginButton.BeginAnimation(OpacityProperty, elementHide);
            loginInputBox.BeginAnimation(OpacityProperty, elementHide);
            passwordInputBox.BeginAnimation(OpacityProperty, elementHide);
            savePasswordCheckBox.BeginAnimation(OpacityProperty, elementHide);
            loginIcon.BeginAnimation(OpacityProperty, elementHide);
            passwordIcon.BeginAnimation(OpacityProperty, elementHide);

            UnBlurBackground();
        }

        void ShowCapchaWindow()
        {
            capchaButton.BeginAnimation(OpacityProperty, elementShow);
            capchaInputBox.BeginAnimation(OpacityProperty, elementShow);
            capchaImage.BeginAnimation(OpacityProperty, elementShow);

            BlurBackground();
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            RestartApp();
        }

        private void MessageEnterBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string Message = MessageEnterBox.Text;
                MessageEnterBox.Text = "";

                SendMessage(Message);
            }
        }

        async void SendMessage(string message)
        {
            ShowLoadingScreen();

            MessageAttachment.Clear();

            if (speechButton.IsChecked == true)
            {
                string path = FormatPath(Environment.CurrentDirectory + "\\AudioCache\\" + message + ".wav");

                SpeechSynthesizer Synth = new SpeechSynthesizer();
                Synth.SetOutputToWaveFile(path);
                Synth.Rate = 1;
                Prompt prompt = Synth.SpeakAsync(message);

                while (!prompt.IsCompleted)
                {

                }

                Synth.Dispose();

                byte[] file = File.ReadAllBytes(path);
                MessageAttachment.Add(await LoadDocumentToChatAsync(api, file, DocMessageType.AudioMessage, ChatID, message));

                await api.Messages.SendAsync(new MessagesSendParams
                {
                    UserId = ChatID,
                    Attachments = MessageAttachment,
                    RandomId = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });

                File.Delete(path);
            }
            else
            {
                await api.Messages.SendAsync(new MessagesSendParams
                {
                    UserId = ChatID,
                    Message = message,
                    RandomId = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            }

            if (message != "" && speechButton.IsChecked == true)
                Chat.Text += "\n\n" + message + "(голос) ---- Вы";
            else
                Chat.Text += "\n\n" + message + " ---- Вы";

            Chat.ScrollToEnd();

            HideLoadingScreen();
        }

        string FormatPath(string path)
        {
            char[] badSymbols = { '*', '"', '?', '>', '<', '|', '*', '+' };

            foreach (char symbol in badSymbols)
            {
                path = path.Replace(symbol, ' ');
            }

            if (path.Length > 245)
                path = path.Remove(245, path.Length - 245);

            return path;
        }

        void HideCapchaWindow()
        {
            capchaButton.BeginAnimation(OpacityProperty, elementHide);
            capchaInputBox.BeginAnimation(OpacityProperty, elementHide);
            capchaImage.BeginAnimation(OpacityProperty, elementHide);

            UnBlurBackground();
        }

        void BlurBackground()
        {
            Storyboard sb = new Storyboard();
            Storyboard.SetTarget(backgroundBlur, Background);
            Storyboard.SetTargetProperty(backgroundBlur, new PropertyPath("Effect.Radius"));

            sb.Children.Add(backgroundBlur);
            sb.Begin();
        }

        void UnBlurBackground()
        {
            Storyboard sb = new Storyboard();
            Storyboard.SetTarget(backgroundUnBlur, Background);
            Storyboard.SetTargetProperty(backgroundUnBlur, new PropertyPath("Effect.Radius"));

            sb.Children.Add(backgroundUnBlur);
            sb.Begin();
        }

        private async void capchaButton_Click(object sender, RoutedEventArgs e)
        {
            HideCapchaWindow();

            ShowLoadingScreen();

            await api.AuthorizeAsync(new ApiAuthParams
            {
                ApplicationId = 6630347,
                Login = Login,
                Password = Password,
                Settings = Settings.All,
                CaptchaKey = Convert.ToString(capchaInputBox.Text),
                CaptchaSid = _cex.Sid
            });

            HideLoadingScreen();
        }

        /// <summary>
        /// Загружает документ на сервер ВК.
        /// </summary>
        /// <param name="vkApi">Вк апи.</param>
        /// <param name="data">Аттачмент, байты которого будут отправлены на сервер</param>
        /// <param name="docMessageType">Тип документа - документ или аудиосообщение.</param>
        /// <param name="peerId">Идентификатор назначения</param>
        /// <param name="filename">Итоговое название документа</param>
        /// <returns>Аттачмент для отправки вместе с сообщением.</returns>
        public async Task<MediaAttachment> LoadDocumentToChatAsync(VkApi vkApi, byte[] data,
            DocMessageType docMessageType, long peerId, string filename)
        {
            var uploadServer = vkApi.Docs.GetMessagesUploadServer(peerId, docMessageType);

            var r = await UploadFile(uploadServer.UploadUrl, data);
            var documents = vkApi.Docs.Save(r, filename ?? Guid.NewGuid().ToString());

            if (documents.Count != 1)
                throw new ArgumentException($"Error while loading document attachment to {uploadServer.UploadUrl}");

            return documents[0];
        }

        /// <summary>
        /// Загружает массив байт на указанный url
        /// </summary>
        /// <param name="url">Адрес для загрузки</param>
        /// <param name="data">Массив данных для загрузки</param>
        /// <returns>Строка, которую вернул сервер.</returns>
        public async Task<string> UploadFile(string url, byte[] data)
        {
            using (var client = new HttpClient())
            {
                var requestContent = new MultipartFormDataContent();
                var documentContent = new ByteArrayContent(data);
                documentContent.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
                requestContent.Add(documentContent, "file", "audio.webm");

                var response = await client.PostAsync(url, requestContent);

                return Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync());
            }
        }
    }

    public class RC4
    {
        byte[] S = new byte[256];

        int x = 0;
        int y = 0;

        public RC4(byte[] key)
        {
            init(key);
        }

        private void init(byte[] key)
        {
            int keyLength = key.Length;

            for (int i = 0; i < 256; i++)
            {
                S[i] = (byte)i;
            }

            int j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (j + S[i] + key[i % keyLength]) % 256;
                S.Swap(i, j);
            }
        }

        public byte[] Encode(byte[] dataB, int size)
        {
            byte[] data = dataB.Take(size).ToArray();

            byte[] cipher = new byte[data.Length];

            for (int m = 0; m < data.Length; m++)
            {
                cipher[m] = (byte)(data[m] ^ keyItem());
            }

            return cipher;
        }
        public byte[] Decode(byte[] dataB, int size)
        {
            return Encode(dataB, size);
        }

        private byte keyItem()
        {
            x = (x + 1) % 256;
            y = (y + S[x]) % 256;

            S.Swap(x, y);

            return S[(S[x] + S[y]) % 256];
        }
    }

    static class SwapExt
    {
        public static void Swap<T>(this T[] array, int index1, int index2)
        {
            T temp = array[index1];
            array[index1] = array[index2];
            array[index2] = temp;
        }
    }


}

public class LongPollResponse
{
    public ulong ts;
    public string[][] updates;
}