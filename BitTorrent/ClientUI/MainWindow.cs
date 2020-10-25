//
// Author: Robert Tizzard
//
// Programs: Simple console application to use BitTorrent class library.
//
// Description: 
//
// Copyright 2020.
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BitTorrentLibrary;
using Terminal.Gui;

namespace ClientUI
{
    /// <summary>
    /// 
    /// </summary>
    public class MainWindow : Window
    {
        private MenuBar _mainMenuBar;
        private readonly Label torrentFileLabel;
        private readonly Label _progressBarBeginText;
        private readonly Label _progressBarEndText;
        public MenuBar MainMenuBar { get => _mainMenuBar; set => _mainMenuBar = value; }
        public TorrentFileNameText TorrentFileText { get; set; }
        public ProgressBar DownloadProgress { get; set; }
        public DownloadButton DownloadButton { get; set; }
        public InformationWindow InformationWindow { get; set; }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public MainWindow(string name) : base(name)
        {
            List<View> viewables = new List<View>();

            torrentFileLabel = new Label("Torrent File: ")
            {
                X = 1,
                Y = 1
            };
            viewables.Add(torrentFileLabel);

            TorrentFileText = new TorrentFileNameText()
            {
                X = Pos.Right(torrentFileLabel),
                Y = Pos.Top(torrentFileLabel),
                Width = 50,
            };
            viewables.Add(TorrentFileText);

            _progressBarBeginText = new Label("Progress : [")
            {
                X = Pos.Left(torrentFileLabel),
                Y = Pos.Bottom(torrentFileLabel) + 1,
            };
            viewables.Add(_progressBarBeginText);

            DownloadProgress = new ProgressBar()
            {
                X = Pos.Right(_progressBarBeginText),
                Y = Pos.Bottom(torrentFileLabel) + 1,
                Width = 60,
                Height = 1
            };
            viewables.Add(DownloadProgress);

            _progressBarEndText = new Label("]")
            {
                X = Pos.Right(DownloadProgress) - 1,
                Y = Pos.Bottom(torrentFileLabel) + 1,
            };
            viewables.Add(_progressBarEndText);

            DownloadButton = new DownloadButton("Download", this)
            {
                X = Pos.Right(TorrentFileText),
                Y = Pos.Top(torrentFileLabel)

            };
            viewables.Add(DownloadButton);

            DownloadButton.Clicked += DownloadButton.ButtonPressed;

            InformationWindow = new InformationWindow("Information")
            {
                X = Pos.Left(this),
                Y = Pos.Bottom(_progressBarBeginText) + 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = false
            };
            viewables.Add(InformationWindow);

            foreach (var viewable in viewables)
            {
                Add(viewable);
            }

        }
    }
}