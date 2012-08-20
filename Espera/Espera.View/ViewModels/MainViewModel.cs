﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Timers;
using System.Windows.Input;
using Espera.Core.Management;
using Espera.View.Properties;
using Rareform.Patterns.MVVM;

namespace Espera.View.ViewModels
{
    internal sealed class MainViewModel : ViewModelBase<MainViewModel>, IDisposable
    {
        private readonly Library library;
        private readonly Timer playlistTimeoutUpdateTimer;
        private readonly Timer updateTimer;
        private ISongSourceViewModel currentSongSource;
        private bool displayTimeoutWarning;
        private bool isLocal;
        private bool isYoutube;
        private ObservableCollection<PlaylistViewModel> playlists;
        private IEnumerable<PlaylistEntryViewModel> selectedPlaylistEntries;

        public MainViewModel()
        {
            if (Settings.Default.UpgradeRequired)
            {
                Settings.Default.Upgrade();
                Settings.Default.UpgradeRequired = false;
            }

            this.library = new Library(new RemovableDriveWatcher());
            this.library.Initialize();

            this.library.SongStarted += LibraryRaisedSongStarted;
            this.library.SongFinished += LibraryRaisedSongFinished;
            this.library.AccessModeChanged += (sender, e) => this.UpdateUserAccess();
            this.library.PlaylistChanged += (sender, e) => this.UpdatePlaylist();

            if (!this.library.Playlists.Any())
            {
                this.library.AddAndSwitchToPlaylist(this.GetNewPlaylistName());
            }

            else
            {
                this.library.SwitchToPlaylist(this.library.Playlists.First().Name);
            }

            this.AdministratorViewModel = new AdministratorViewModel(this.library);

            this.LocalViewModel = new LocalViewModel(this.library);
            this.LocalViewModel.TimeoutWarning += (sender, e) => this.TriggerTimeoutWarning();

            this.YoutubeViewModel = new YoutubeViewModel(this.library);
            this.YoutubeViewModel.TimeoutWarning += (sender, e) => this.TriggerTimeoutWarning();

            this.updateTimer = new Timer(333);
            this.updateTimer.Elapsed += (sender, e) => this.UpdateCurrentTime();

            this.playlistTimeoutUpdateTimer = new Timer(333);
            this.playlistTimeoutUpdateTimer.Elapsed += (sender, e) => this.UpdateRemainingPlaylistTimeout();
            this.playlistTimeoutUpdateTimer.Start();
        }

        public ICommand AddPlaylistCommand
        {
            get
            {
                return new RelayCommand
                (
                    param => this.AddPlaylist()
                );
            }
        }

        public AdministratorViewModel AdministratorViewModel { get; private set; }

        public bool CanChangeTime
        {
            get { return this.library.CanChangeTime; }
        }

        public bool CanChangeVolume
        {
            get { return this.library.CanChangeVolume; }
        }

        public bool CanSwitchPlaylist
        {
            get { return this.library.CanSwitchPlaylist; }
        }

        public bool CanUseYoutube
        {
            get { return !this.library.StreamYoutube || RegistryHelper.IsVlcInstalled(); }
        }

        public PlaylistViewModel CurrentEditedPlaylist
        {
            get { return this.Playlists.SingleOrDefault(playlist => playlist.EditName); }
        }

        public PlaylistViewModel CurrentPlaylist
        {
            get { return this.playlists == null ? null : this.playlists.Single(vm => vm.Name == this.library.CurrentPlaylist.Name); }
            set
            {
                if (value != null) // There always has to be a playlist selected
                {
                    this.library.SwitchToPlaylist(value.Name);
                    this.OnPropertyChanged(vm => vm.CurrentPlaylist);
                }
            }
        }

        public int CurrentSeconds
        {
            get { return (int)this.CurrentTime.TotalSeconds; }
            set { this.library.CurrentTime = TimeSpan.FromSeconds(value); }
        }

        public ISongSourceViewModel CurrentSongSource
        {
            get { return this.currentSongSource; }
            private set
            {
                if (this.CurrentSongSource != value)
                {
                    this.currentSongSource = value;
                    this.OnPropertyChanged(vm => vm.CurrentSongSource);
                }
            }
        }

        public TimeSpan CurrentTime
        {
            get { return this.library.CurrentTime; }
        }

        public bool DisplayTimeoutWarning
        {
            get { return this.displayTimeoutWarning; }
            set
            {
                if (this.displayTimeoutWarning != value)
                {
                    this.displayTimeoutWarning = value;
                    this.OnPropertyChanged(vm => vm.DisplayTimeoutWarning);
                }
            }
        }

        public ICommand EditPlaylistNameCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        this.CurrentPlaylist.EditName = true;
                    }
                );
            }
        }

        public bool IsAdmin
        {
            get { return this.library.AccessMode == AccessMode.Administrator; }
        }

        public bool IsLocal
        {
            get { return this.isLocal; }
            set
            {
                if (this.IsLocal != value)
                {
                    this.isLocal = value;
                    this.OnPropertyChanged(vm => vm.IsLocal);

                    if (this.IsLocal)
                    {
                        this.CurrentSongSource = this.LocalViewModel;
                    }
                }
            }
        }

        public bool IsPlaying
        {
            get { return this.library.IsPlaying; }
        }

        public bool IsYoutube
        {
            get { return this.isYoutube; }
            set
            {
                if (this.IsYoutube != value)
                {
                    this.isYoutube = value;
                    this.OnPropertyChanged(vm => vm.IsYoutube);

                    if (this.IsYoutube)
                    {
                        this.CurrentSongSource = this.YoutubeViewModel;
                    }
                }
            }
        }

        public LocalViewModel LocalViewModel { get; private set; }

        /// <summary>
        /// Sets the volume to the lowest possible value.
        /// </summary>
        public ICommand MuteCommand
        {
            get
            {
                return new RelayCommand
                (
                    param => this.Volume = 0,
                    param => this.IsAdmin
                );
            }
        }

        /// <summary>
        /// Plays the next song in the playlist.
        /// </summary>
        public ICommand NextSongCommand
        {
            get
            {
                return new RelayCommand
                (
                    param => this.library.PlayNextSong(),
                    param => this.IsAdmin && this.library.CanPlayNextSong
                );
            }
        }

        /// <summary>
        /// Pauses the currently played song.
        /// </summary>
        public ICommand PauseCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        this.library.PauseSong();
                        this.updateTimer.Stop();
                        this.OnPropertyChanged(vm => vm.IsPlaying);
                    },
                    param => (this.IsAdmin || !this.library.LockPlayPause) && this.IsPlaying
                );
            }
        }

        /// <summary>
        /// Plays the song that is currently selected in the playlist or continues the song if it is paused.
        /// </summary>
        public ICommand PlayCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        /*
                         * Some callers want to ignore if a song is paused and want to play the currently
                         * selected song, instead of continuing the currently paused song.
                         * This decision is passed as parameter.
                         *
                         * XAML commands pass the parameter as string, code-behind commands pass the parameter
                         * as boolean, so we have to check what type it is.
                         */
                        var value = param as string;

                        bool ignorePause = value == null ? (bool)param : Boolean.Parse(value);

                        if (this.library.IsPaused && !ignorePause)
                        {
                            this.library.ContinueSong();
                            this.updateTimer.Start();
                            this.OnPropertyChanged(vm => vm.IsPlaying);
                        }

                        else
                        {
                            this.library.PlaySong(this.SelectedPlaylistEntries.First().Index);
                        }
                    },
                    param => (this.IsAdmin || !this.library.LockPlayPause) &&
                        ((this.SelectedPlaylistEntries != null && this.SelectedPlaylistEntries.Count() == 1) ||
                        (this.library.LoadedSong != null || this.library.IsPaused))
                );
            }
        }

        public int PlaylistAlbumColumnWidth
        {
            get { return Settings.Default.PlaylistAlbumColumnWidth; }
            set { Settings.Default.PlaylistAlbumColumnWidth = value; }
        }

        public int PlaylistArtistColumnWidth
        {
            get { return Settings.Default.PlaylistArtistColumnWidth; }
            set { Settings.Default.PlaylistArtistColumnWidth = value; }
        }

        public int PlaylistCachingProgressColumnWidth
        {
            get { return Settings.Default.PlaylistCachingProgressColumnWidth; }
            set { Settings.Default.PlaylistCachingProgressColumnWidth = value; }
        }

        public int PlaylistDurationColumnWidth
        {
            get { return Settings.Default.PlaylistDurationColumnWidth; }
            set { Settings.Default.PlaylistDurationColumnWidth = value; }
        }

        public int PlaylistGenreColumnWidth
        {
            get { return Settings.Default.PlaylistGenreColumnWidth; }
            set { Settings.Default.PlaylistGenreColumnWidth = value; }
        }

        // Save the playlist height as string, so that the initial value can be "*"
        public string PlaylistHeight
        {
            get { return Settings.Default.PlaylistHeight; }
            set { Settings.Default.PlaylistHeight = value; }
        }

        public ObservableCollection<PlaylistViewModel> Playlists
        {
            get
            {
                var vms = this.library.Playlists.Select(this.CreatePlaylistViewModel);

                return this.playlists ??
                    (this.playlists = new ObservableCollection<PlaylistViewModel>(vms));
            }
        }

        public int PlaylistSourceColumnWidth
        {
            get { return Settings.Default.PlaylistSourceColumnWidth; }
            set { Settings.Default.PlaylistSourceColumnWidth = value; }
        }

        public int PlaylistTitleColumnWidth
        {
            get { return Settings.Default.PlaylistTitleColumnWidth; }
            set { Settings.Default.PlaylistTitleColumnWidth = value; }
        }

        /// <summary>
        /// Plays the song that is before the currently played song in the playlist.
        /// </summary>
        public ICommand PreviousSongCommand
        {
            get
            {
                return new RelayCommand
                (
                    param => this.library.PlayPreviousSong(),
                    param => this.IsAdmin && this.library.CanPlayPreviousSong
                );
            }
        }

        public TimeSpan RemainingPlaylistTimeout
        {
            get { return this.library.RemainingPlaylistTimeout; }
        }

        public ICommand RemovePlaylistCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        int index = this.Playlists.IndexOf(this.CurrentPlaylist);

                        this.library.RemovePlaylist(this.CurrentPlaylist.Name);

                        this.Playlists.Remove(this.CurrentPlaylist);

                        if (!this.library.Playlists.Any())
                        {
                            this.AddPlaylist();
                        }

                        if (this.Playlists.Count > index)
                        {
                            this.CurrentPlaylist = this.Playlists[index];
                        }

                        else if (this.Playlists.Count >= 1)
                        {
                            this.CurrentPlaylist = this.Playlists[index - 1];
                        }

                        else
                        {
                            this.CurrentPlaylist = this.Playlists[0];
                        }

                        this.OnPropertyChanged(vm => vm.Playlists);
                    },
                    param => this.CurrentEditedPlaylist != null || this.CurrentPlaylist != null
                );
            }
        }

        public ICommand RemoveSelectedPlaylistEntriesCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        this.library.RemoveFromPlaylist(this.SelectedPlaylistEntries.Select(entry => entry.Index));

                        this.OnPropertyChanged(vm => vm.CurrentPlaylist);
                        this.OnPropertyChanged(vm => vm.SongsRemaining);
                        this.OnPropertyChanged(vm => vm.TimeRemaining);
                    },
                    param => this.SelectedPlaylistEntries != null
                        && this.SelectedPlaylistEntries.Any()
                        && (this.IsAdmin || !this.library.LockPlaylistRemoval)
                );
            }
        }

        public IEnumerable<PlaylistEntryViewModel> SelectedPlaylistEntries
        {
            get { return this.selectedPlaylistEntries; }
            set
            {
                if (this.SelectedPlaylistEntries != value)
                {
                    this.selectedPlaylistEntries = value;
                    this.OnPropertyChanged(vm => vm.SelectedPlaylistEntries);
                    this.OnPropertyChanged(vm => vm.PlayCommand);
                }
            }
        }

        public ICommand ShufflePlaylistCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        this.library.ShufflePlaylist();

                        this.UpdatePlaylist();
                    }
                );
            }
        }

        /// <summary>
        /// Gets the number of songs that come after the currently played song.
        /// </summary>
        public int SongsRemaining
        {
            get
            {
                return this.CurrentPlaylist.Songs
                    .SkipWhile(song => song.IsInactive)
                    .Count();
            }
        }

        /// <summary>
        /// Gets the total remaining time of all songs that come after the currently played song.
        /// </summary>
        public TimeSpan? TimeRemaining
        {
            get
            {
                var songs = this.CurrentPlaylist.Songs
                    .SkipWhile(song => song.IsInactive)
                    .ToList();

                if (songs.Any())
                {
                    return songs
                        .Select(song => song.Duration)
                        .Aggregate((t1, t2) => t1 + t2);
                }

                return null;
            }
        }

        public int TotalSeconds
        {
            get { return (int)this.TotalTime.TotalSeconds; }
        }

        public TimeSpan TotalTime
        {
            get { return this.library.TotalTime; }
        }

        /// <summary>
        /// Sets the volume to the highest possible value.
        /// </summary>
        public ICommand UnMuteCommand
        {
            get
            {
                return new RelayCommand
                (
                    param => this.Volume = 1,
                    param => this.IsAdmin
                );
            }
        }

        public double Volume
        {
            get { return this.library.Volume; }
            set
            {
                this.library.Volume = (float)value;
                this.OnPropertyChanged(vm => vm.Volume);
            }
        }

        public YoutubeViewModel YoutubeViewModel { get; private set; }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Settings.Default.Save();

            this.library.Save();
            this.library.Dispose();

            this.playlistTimeoutUpdateTimer.Dispose();
            this.updateTimer.Dispose();
        }

        private void AddPlaylist()
        {
            this.library.AddAndSwitchToPlaylist(this.GetNewPlaylistName());

            PlaylistViewModel newPlaylist = this.CreatePlaylistViewModel(this.library.Playlists.Last());
            this.Playlists.Add(newPlaylist);

            this.CurrentPlaylist = newPlaylist;
            this.CurrentPlaylist.EditName = true;
        }

        private PlaylistViewModel CreatePlaylistViewModel(PlaylistInfo playlist)
        {
            return new PlaylistViewModel(playlist, name => this.playlists.Count(p => p.Name == name) == 1);
        }

        private string GetNewPlaylistName()
        {
            string name;

            int i = 1;
            string suffix = String.Empty;

            do
            {
                name = "New Playlist";

                if (i > 1)
                {
                    suffix = " " + i;
                }

                i++;
            }
            while (this.library.Playlists.Any(playlist => playlist.Name == name + suffix));

            return name + suffix;
        }

        private void LibraryRaisedSongFinished(object sender, EventArgs e)
        {
            // We need this check, to avoid that the pause/play button changes its state,
            // when the library starts the next song
            if (!this.library.CanPlayNextSong)
            {
                this.OnPropertyChanged(vm => vm.IsPlaying);
            }

            this.OnPropertyChanged(vm => vm.CurrentPlaylist);

            this.updateTimer.Stop();
        }

        private void LibraryRaisedSongStarted(object sender, EventArgs e)
        {
            this.UpdateTotalTime();

            this.OnPropertyChanged(vm => vm.IsPlaying);
            this.OnPropertyChanged(vm => vm.CurrentPlaylist);

            this.OnPropertyChanged(vm => vm.SongsRemaining);
            this.OnPropertyChanged(vm => vm.TimeRemaining);

            this.updateTimer.Start();
        }

        private void TriggerTimeoutWarning()
        {
            this.DisplayTimeoutWarning = true;
            this.DisplayTimeoutWarning = false;
        }

        private void UpdateCurrentTime()
        {
            this.OnPropertyChanged(vm => vm.CurrentSeconds);
            this.OnPropertyChanged(vm => vm.CurrentTime);
        }

        private void UpdatePlaylist()
        {
            this.OnPropertyChanged(vm => vm.CurrentPlaylist);
            this.OnPropertyChanged(vm => vm.SongsRemaining);
            this.OnPropertyChanged(vm => vm.TimeRemaining);

            if (this.library.EnablePlaylistTimeout)
            {
                this.OnPropertyChanged(vm => vm.RemainingPlaylistTimeout);
            }
        }

        private void UpdateRemainingPlaylistTimeout()
        {
            if (this.RemainingPlaylistTimeout > TimeSpan.Zero)
            {
                this.OnPropertyChanged(vm => vm.RemainingPlaylistTimeout);
            }
        }

        private void UpdateTotalTime()
        {
            this.OnPropertyChanged(vm => vm.TotalSeconds);
            this.OnPropertyChanged(vm => vm.TotalTime);
        }

        private void UpdateUserAccess()
        {
            this.OnPropertyChanged(vm => vm.IsAdmin);
            this.OnPropertyChanged(vm => vm.CanChangeVolume);
            this.OnPropertyChanged(vm => vm.CanChangeTime);
            this.OnPropertyChanged(vm => vm.CanSwitchPlaylist);
        }
    }
}