//
// Author: Rob Tizzard
//
// Programs: A simple console based torrent client.
//
// Description: Class that defines the layout of the applications main window.
//
// Copyright 2020.
//
using System;
using System.Linq;
using System.Collections.Generic;
using Terminal.Gui;
using BitTorrentLibrary;
namespace ClientUI
{
    /// <summary>
    /// Main Application window.
    /// </summary>
    public class MainWindow : Window
    {
        private readonly Label torrentFileLabel;                    // Torrent file field label
        private readonly Label _progressBarBeginText;               // Beginning of progress bar '['
        private readonly Label _progressBarEndText;                 // End of progress bar ']'
        private TorrentContext _selectedSeederTorrent;              // Selected seeder torrent context
        private bool _displaySeederInformationWindow = true;        // == true seeder information window 
        private readonly InformationWindow _seederInformationWindow;// Seeding torrent information sub-window
        private readonly ProgressBar _downloadProgress;             // Downloading progress bar
        public TextField TorrentFileText { get; set; }              // Text field containing torrent file name
        public InformationWindow InfoWindow { get; set; }           // Torrent information sub-window
        public SeedingWindow SeederListWindow { get; set; }         // Seeding torrents sub-window (overlays information)
        public Torrent TorrentHandler { get; set; }                    // Currently active downloading torrent + seeders
        public bool DisplayInformationWindow { get; set; } = true;  // == true information window displayed
        /// <summary>
        /// Update seeder information. This is used as the tracker callback to be invoked
        /// when the next announce response is recieved back for the seeder torrent context
        /// currently selected.
        /// </summary>
        /// <param name="obj"></param>
        private void UpdateSeederInformation(Object obj)
        {
            _seederInformationWindow.Update(((Torrent)obj).GetTorrentDetails(_selectedSeederTorrent));
        }
        /// <summary>
        /// Build main application window including the information
        /// and seeding windows which overlay each other depending which
        /// is toggled to display.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public MainWindow(string name) : base(name)
        {
            TorrentHandler = new Torrent();
            List<View> viewables = new List<View>();
            torrentFileLabel = new Label("Torrent File: ")
            {
                X = 1,
                Y = 1
            };
            viewables.Add(torrentFileLabel);
            TorrentFileText = new TextField()
            {
                X = Pos.Right(torrentFileLabel),
                Y = Pos.Top(torrentFileLabel),
                Width = 50,
            };
            viewables.Add(TorrentFileText);
            TorrentFileText.CursorPosition = TorrentFileText.ToString().Length;
            _progressBarBeginText = new Label("Progress : [")
            {
                X = Pos.Left(torrentFileLabel),
                Y = Pos.Bottom(torrentFileLabel) + 1,
            };
            viewables.Add(_progressBarBeginText);
            _downloadProgress = new ProgressBar()
            {
                X = Pos.Right(_progressBarBeginText),
                Y = Pos.Bottom(torrentFileLabel) + 1,
                Width = 60,
                Height = 1
            };
            viewables.Add(_downloadProgress);
            _progressBarEndText = new Label("]")
            {
                X = Pos.Right(_downloadProgress) - 1,
                Y = Pos.Bottom(torrentFileLabel) + 1,
            };
            viewables.Add(_progressBarEndText);
            InfoWindow = new InformationWindow("Information")
            {
                X = Pos.Left(this),
                Y = Pos.Bottom(_progressBarBeginText) + 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = false
            };
            viewables.Add(InfoWindow);
            SeederListWindow = new SeedingWindow("Seeding")
            {
                X = Pos.Left(this),
                Y = Pos.Bottom(_progressBarBeginText) + 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = false
            };
            _seederInformationWindow = new InformationWindow("Seed Info")
            {
                X = Pos.Left(this),
                Y = Pos.Bottom(_progressBarBeginText) + 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = false
            };
            foreach (var viewable in viewables)
            {
                Add(viewable);
            }
        }
        /// <summary>
        /// Close down main torrent
        /// </summary>
        /// <param name="_main"></param>
        public void ClosedownTorrent()
        {
            TorrentHandler.CloseDownloadingTorrent();
            InfoWindow.ClearData();
        }
        /// <summary>
        /// Toggle seeding list and torrent information window
        /// </summary>
        public void ToggleSeedingList()
        {
            if (DisplayInformationWindow)
            {
                Remove(InfoWindow);
                Add(SeederListWindow);
                DisplayInformationWindow = false;
                SeederListWindow.SetFocus();
            }
            else
            {
                if (!_displaySeederInformationWindow)
                {
                    ToggleSeedinginformation();
                }
                Remove(SeederListWindow);
                Add(InfoWindow);
                DisplayInformationWindow = true;
                TorrentFileText.SetFocus();
            }
        }
        /// <summary>
        /// Toggle seeding list and seeder information window
        /// </summary>
        /// <param name="_main"></param>
        public void ToggleSeedinginformation()
        {
            if (_displaySeederInformationWindow)
            {
                Remove(SeederListWindow);
                Add(_seederInformationWindow);
                _displaySeederInformationWindow = false;
                List<TorrentContext> seeders = new List<TorrentContext>();
                foreach (var torrent in TorrentHandler.TorrentList)
                {
                    if (torrent.Status == TorrentStatus.Seeding)
                    {
                        seeders.Add(torrent);
                    }
                }
                if (seeders.Count > 0)
                {
                    _selectedSeederTorrent = TorrentHandler.TorrentList.ToArray()[SeederListWindow.SeederListView.SelectedItem];
                    _seederInformationWindow.SetTracker(_selectedSeederTorrent.MainTracker.TrackerURL);
                    UpdateSeederInformation(TorrentHandler);
                    _selectedSeederTorrent.MainTracker.SetSeedingInterval(2 * 1000);
                    _selectedSeederTorrent.MainTracker.CallBack = UpdateSeederInformation;
                    _selectedSeederTorrent.MainTracker.CallBackData = TorrentHandler;
                    _seederInformationWindow.SetFocus();
                }
            }
            else
            {
                if (_selectedSeederTorrent != null)
                {
                    _seederInformationWindow.ClearData();
                    _selectedSeederTorrent.MainTracker.SetSeedingInterval(60000 * 30);
                    _selectedSeederTorrent.MainTracker.CallBack = null;
                    _selectedSeederTorrent.MainTracker.CallBack = null;
                    Remove(_seederInformationWindow);
                    _selectedSeederTorrent = null;
                }
                Add(SeederListWindow);
                _displaySeederInformationWindow = true;
                SeederListWindow.SetFocus();
            }
        }
        /// <summary>
        /// Update download progress bar
        /// </summary>
        /// <param name="progress"></param>
        public void UpdatProgressBar(float progress)
        {
            _downloadProgress.Fraction = progress;
        }
    }
}
