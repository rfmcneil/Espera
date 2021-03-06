﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Rareform.Reflection;
using Rareform.Validation;

namespace Espera.Core.Management
{
    /// <summary>
    /// Represents a playlist where songs are stored with an associated index.
    /// </summary>
    internal sealed class Playlist : IEnumerable<Song>
    {
        private Dictionary<int, Song> playlist;

        /// <summary>
        /// Initializes a new instance of the <see cref="Playlist"/> class.
        /// </summary>
        public Playlist(string name)
        {
            this.Name = name;
            this.playlist = new Dictionary<int, Song>();
        }

        /// <summary>
        /// Gets a value indicating whether the next song in the playlist can be played.
        /// </summary>
        /// <value>
        /// true if the next song in the playlist can be played; otherwise, false.
        /// </value>
        public bool CanPlayNextSong
        {
            get { return this.CurrentSongIndex.HasValue && this.playlist.ContainsKey(this.CurrentSongIndex.Value + 1); }
        }

        /// <summary>
        /// Gets a value indicating whether the previous song in the playlist can be played.
        /// </summary>
        /// <value>
        /// true if the previous song in the playlist can be played; otherwise, false.
        /// </value>
        public bool CanPlayPreviousSong
        {
            get { return this.CurrentSongIndex.HasValue && this.playlist.ContainsKey(this.CurrentSongIndex.Value - 1); }
        }

        /// <summary>
        /// Gets the index of the currently played song in the playlist.
        /// </summary>
        /// <value>
        /// The index of the currently played song in the playlist.
        /// </value>
        public int? CurrentSongIndex { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// Gets the <see cref="Espera.Core.Song"/> at the specified index.
        /// </summary>
        public Song this[int index]
        {
            get { return this.playlist[index]; }
        }

        /// <summary>
        /// Adds the specified songs to end of the playlist.
        /// </summary>
        /// <param name="songList">The songs to add to the end of the playlist.</param>
        public void AddSongs(IEnumerable<Song> songList)
        {
            if (songList == null)
                Throw.ArgumentNullException(() => songList);

            foreach (Song song in songList)
            {
                if (song.HasToCache && !song.IsCaching)
                {
                    GlobalSongCacheQueue.Instance.Enqueue(song);
                }

                int index = this.playlist.Keys.Count == 0 ? 0 : this.playlist.Keys.Max() + 1;

                this.playlist.Add(index, song);
            }
        }

        public bool ContainsIndex(int songIndex)
        {
            return this.playlist.ContainsKey(songIndex);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<Song> GetEnumerator()
        {
            return this.playlist
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value)
                .GetEnumerator();
        }

        /// <summary>
        /// Gets the index in the playlist for each of the specified songs.
        /// </summary>
        /// <param name="songs">The songs.</param>
        /// <returns></returns>
        public IEnumerable<int> GetIndexes(IEnumerable<Song> songs)
        {
            return this.playlist
                .Where(entry => songs.Contains(entry.Value))
                .Select(entry => entry.Key)
                .ToList();
        }

        /// <summary>
        /// Inserts a song from a specified index to a other index in the playlist and moves all songs in between these indexes one index back.
        /// </summary>
        /// <param name="fromIndex">The index of the song to move.</param>
        /// <param name="toIndex">To index to insert the song.</param>
        public void InsertMove(int fromIndex, int toIndex)
        {
            if (fromIndex < 0)
                Throw.ArgumentOutOfRangeException(() => fromIndex, 0);

            if (toIndex < 0)
                Throw.ArgumentOutOfRangeException(() => toIndex, 0);

            if (toIndex >= fromIndex)
                Throw.ArgumentException(
                    String.Format("{0} has to be smaller than {1}",
                    Reflector.GetMemberName(() => toIndex), Reflector.GetMemberName(() => fromIndex)),
                    () => toIndex);

            Song from = this[fromIndex];

            for (int i = fromIndex; i > toIndex; i--)
            {
                this.playlist[i] = this[i - 1];
            }

            this.playlist[toIndex] = from;
        }

        /// <summary>
        /// Removes the songs with the specified indexes from the <see cref="Playlist"/>.
        /// </summary>
        /// <param name="indexes">The indexes of the songs to remove.</param>
        public void RemoveSongs(IEnumerable<int> indexes)
        {
            if (indexes == null)
                Throw.ArgumentNullException(() => indexes);

            foreach (int index in indexes)
            {
                if (index == this.CurrentSongIndex)
                {
                    this.CurrentSongIndex = null;
                }

                this.playlist.Remove(index);
            }

            this.Rebuild();
        }

        public void Shuffle()
        {
            int count = this.playlist.Count;

            var random = new Random();

            for (int index = 0; index < count; index++)
            {
                int newIndex = random.Next(count);

                // Migrate the CurrentSongIndex to the new position
                if (index == this.CurrentSongIndex)
                {
                    this.CurrentSongIndex = newIndex;
                }

                else if (newIndex == this.CurrentSongIndex)
                {
                    this.CurrentSongIndex = index;
                }

                Song temp = this.playlist[index];

                this.playlist[index] = this.playlist[newIndex];

                this.playlist[newIndex] = temp;
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.IEnumerator"/> object that can be used to iterate through the collection.
        /// </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Rebuilds the playlist with new indexes.
        /// </summary>
        private void Rebuild()
        {
            var newPlaylist = new Dictionary<int, Song>();
            int index = 0;

            foreach (var entry in playlist.OrderBy(entry => entry.Key))
            {
                newPlaylist.Add(index, entry.Value);

                if (this.CurrentSongIndex == entry.Key)
                {
                    this.CurrentSongIndex = index;
                }

                index++;
            }

            this.playlist = newPlaylist;
        }
    }
}