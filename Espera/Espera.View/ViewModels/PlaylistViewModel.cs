﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Espera.Core.Management;
using Rareform.Patterns.MVVM;
using Rareform.Reflection;

namespace Espera.View.ViewModels
{
    internal sealed class PlaylistViewModel : ViewModelBase<PlaylistViewModel>, IDataErrorInfo
    {
        private readonly PlaylistInfo playlist;
        private readonly Func<string, bool> renameRequest;
        private bool editName;
        private string saveName;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaylistViewModel"/> class.
        /// </summary>
        /// <param name="playlist">The playlist info.</param>
        /// <param name="renameRequest">A function that requests the rename of the playlist. Return true, if the rename is granted, otherwise false.</param>
        public PlaylistViewModel(PlaylistInfo playlist, Func<string, bool> renameRequest)
        {
            this.playlist = playlist;
            this.renameRequest = renameRequest;
        }

        public bool EditName
        {
            get { return this.editName; }
            set
            {
                if (this.EditName != value)
                {
                    this.editName = value;

                    if (this.EditName)
                    {
                        this.saveName = this.Name;
                    }

                    else if (this.HasErrors())
                    {
                        this.Name = this.saveName;
                        this.saveName = null;
                    }

                    this.OnPropertyChanged(vm => vm.EditName);
                }
            }
        }

        public string Error
        {
            get { return null; }
        }

        public string Name
        {
            get { return this.playlist.Name; }
            set
            {
                if (this.Name != value)
                {
                    this.playlist.Name = value;
                    this.OnPropertyChanged(vm => vm.Name);
                }
            }
        }

        public IEnumerable<PlaylistEntryViewModel> Songs
        {
            get
            {
                var songs = this.playlist.Songs
                    .Select((song, index) => new PlaylistEntryViewModel(song, index))
                    .ToList(); // We want a list, so that ReSharper doesn't complain about multiple enumerations

                if (this.playlist.CurrentSongIndex.HasValue)
                {
                    PlaylistEntryViewModel entry = songs[this.playlist.CurrentSongIndex.Value];

                    if (!entry.IsCorrupted)
                    {
                        entry.IsPlaying = true;
                    }

                    // If there are more than 5 songs from the beginning of the playlist to the current played song,
                    // skip all, but 5 songs to the position of the currently played song
                    if (songs.TakeWhile(song => !song.IsPlaying).Count() > 5)
                    {
                        songs = songs.Skip(this.playlist.CurrentSongIndex.Value - 5).ToList();
                    }

                    foreach (var model in songs.TakeWhile(song => !song.IsPlaying))
                    {
                        model.IsInactive = true;
                    }
                }

                return songs;
            }
        }

        public string this[string columnName]
        {
            get
            {
                string error = null;

                if (columnName == Reflector.GetMemberName(() => this.Name))
                {
                    if (!this.renameRequest(this.Name))
                    {
                        error = "Name already exists.";
                    }

                    else if (String.IsNullOrWhiteSpace(this.Name))
                    {
                        error = "Name cannot be empty or whitespace.";
                    }
                }

                return error;
            }
        }

        private bool HasErrors()
        {
            return this.GetType().GetProperties().Any(p => this[p.Name] != null);
        }
    }
}