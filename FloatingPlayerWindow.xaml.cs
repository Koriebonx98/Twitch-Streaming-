using System.Windows;
using LibVLCSharp.Shared;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Twich
{
    public partial class FloatingPlayerWindow : Window
    {
        public LibVLC? LibVlc { get; private set; }
        public VlcMediaPlayer? MediaPlayer { get; private set; }

        public FloatingPlayerWindow(LibVLC libVlc, VlcMediaPlayer mediaPlayer)
        {
            InitializeComponent();
            LibVlc = libVlc;
            MediaPlayer = mediaPlayer;
            VideoView.MediaPlayer = MediaPlayer;
        }
    }
}
