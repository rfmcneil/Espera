﻿using System;
using System.IO;
using System.Threading;
using Rareform.Validation;
using Vlc.DotNet.Core;
using Vlc.DotNet.Core.Interops.Signatures.LibVlc.Media;
using Vlc.DotNet.Core.Medias;
using Vlc.DotNet.Wpf;

namespace Espera.Core.Audio
{
    /// <summary>
    /// An <see cref="AudioPlayer"/> that streams songs from YouTube.
    /// </summary>
    internal sealed class YoutubeAudioPlayer : AudioPlayer
    {
        private readonly VlcControl player;
        private TimeSpan? currentTime;

        public YoutubeAudioPlayer(YoutubeSong song)
        {
            if (song == null)
                Throw.ArgumentNullException(() => song);

            string vlcPath = ApplicationHelper.DetectVlcFolderPath();

            VlcContext.LibVlcDllsPath = vlcPath;
            VlcContext.LibVlcPluginsPath = Path.Combine(vlcPath, "plugins");

            VlcContext.StartupOptions.IgnoreConfig = true;

            VlcContext.Initialize();

            this.player = new VlcControl();

            this.player.TimeChanged += (sender, e) => this.CheckSongFinished();
        }

        public override TimeSpan CurrentTime
        {
            get { return this.currentTime ?? this.player.Time; }
            set { this.player.Time = value; }
        }

        public override AudioPlayerState PlaybackState
        {
            get
            {
                switch (this.player.State)
                {
                    case States.Playing:
                    case States.Buffering:
                    case States.Opening:
                        return AudioPlayerState.Playing;
                    case States.Paused:
                        return AudioPlayerState.Paused;
                    case States.Stopped:
                    case States.Ended:
                        return AudioPlayerState.Stopped;
                    default:
                        return AudioPlayerState.None;
                }
            }
        }

        public override TimeSpan TotalTime
        {
            get { return this.Song.Duration; }
        }

        public override float Volume
        {
            get { return this.player.AudioProperties.Volume / 100.0f; }
            set { this.player.AudioProperties.Volume = (int)(value * 100); }
        }

        public override void Dispose()
        {
            this.Stop();
            this.player.Dispose();
            VlcContext.CloseAll();
        }

        public override void Load()
        {
            this.player.Media = new LocationMedia(this.Song.StreamingPath);

            base.Load();
        }

        public override void Pause()
        {
            // We need to temporarly store the current time, because when paused, the player sets it to 0
            this.currentTime = this.CurrentTime;

            this.player.Pause();

            // Wait till the player has finally paused
            // This is a workaround, because the VlcControl does not raise the Paused event properly
            while (this.PlaybackState != AudioPlayerState.Paused)
            {
                Thread.Sleep(100);
            }
        }

        public override void Play()
        {
            this.currentTime = null;

            this.player.Play();

            // Wait till the player is playing
            // We need this, because the playback state sometimes doesn't get updated immediately
            while (this.PlaybackState != AudioPlayerState.Playing)
            {
                Thread.Sleep(100);
            }
        }

        public override void Stop()
        {
            this.player.Stop();
        }

        private void CheckSongFinished()
        {
            // HACK: Cut the time one second before it really ends, so that the current time reaches the duration
            // Also we have to check if the players duration isn't zero, because the TimeChanged event sometimes fires at the beginning of the song
            if (this.player.Duration != TimeSpan.Zero && this.player.Time >= this.player.Duration - TimeSpan.FromSeconds(1))
            {
                while (this.player.IsPlaying)
                {
                    Thread.Sleep(100);
                }

                this.OnSongFinished(EventArgs.Empty);
            }
        }
    }
}