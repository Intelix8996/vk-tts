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

namespace vk_tts
{
    public partial class MainWindow : Window
    {
        DoubleAnimation elementShow, elementHide, backgroundBlur, backgroundUnBlur;

        VkApi api;
        VkNet.Exception.CaptchaNeededException _cex;

        string Login, Password;

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
            Password = passwordInputBox.Text;

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

            BlurBackground();
        }

        void HideLoginField()
        {
            loginButton.BeginAnimation(OpacityProperty, elementHide);
            loginInputBox.BeginAnimation(OpacityProperty, elementHide);
            passwordInputBox.BeginAnimation(OpacityProperty, elementHide);
            savePasswordCheckBox.BeginAnimation(OpacityProperty, elementHide);

            UnBlurBackground();
        }

        void ShowCapchaWindow()
        {
            capchaButton.BeginAnimation(OpacityProperty, elementShow);
            capchaInputBox.BeginAnimation(OpacityProperty, elementShow);
            capchaImage.BeginAnimation(OpacityProperty, elementShow);

            BlurBackground();
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


