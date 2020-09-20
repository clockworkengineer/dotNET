﻿//
// Author: Robert Tizzard
//
// Programs: Simple console application to use BitTorrent class library.
//
// Description: 
//
// Copyright 2019.
//

using System;
using System.Text;
using System.Threading;
using NLog;

namespace BitTorrent
{
    class Program
    {
        public static void AnnouceResponse(AnnounceResponse response)
        {
            BitTorrent.Log.Logger.Debug("\nAnnouce Response\n-------------");
            BitTorrent.Log.Logger.Debug("Status Message: " + response.statusMessage);
            BitTorrent.Log.Logger.Debug("Interval: " + response.interval);
            BitTorrent.Log.Logger.Debug("Min Interval: " + response.minInterval);
            BitTorrent.Log.Logger.Debug("trackerID: " + response.trackerID);
            BitTorrent.Log.Logger.Debug("Complete: " + response.complete);
            BitTorrent.Log.Logger.Debug("Incomplete: " + response.incomplete);
            BitTorrent.Log.Logger.Debug("\nPeers\n------");
            foreach (var peer in response.peers)
            {
                if (peer._peerID != string.Empty)
                {
                    BitTorrent.Log.Logger.Debug("Peer ID: " + peer._peerID);
                }
                BitTorrent.Log.Logger.Debug("IP: " + peer.ip);
                BitTorrent.Log.Logger.Debug("Port: " + peer.port);
            }
        }

        public static void TorrentHasInfo(MetaInfoFile metaFile)
        {
            byte[] infoHash = metaFile.MetaInfoDict["info hash"];

            StringBuilder hex = new StringBuilder(infoHash.Length);
            foreach (byte b in infoHash)
                hex.AppendFormat("{0:x2}", b);

            BitTorrent.Log.Logger.Debug("\nInfo Hash\n-----------\n");
            BitTorrent.Log.Logger.Debug(hex);
        }

        public static void TorrentTrackers(MetaInfoFile metaFile)
        {
            byte[] tracker = metaFile.MetaInfoDict["announce"];

            BitTorrent.Log.Logger.Debug("\nTrackers\n--------\n");
            BitTorrent.Log.Logger.Debug(Encoding.ASCII.GetString(tracker));

            if (metaFile.MetaInfoDict.ContainsKey("announce-list"))
            {
                byte[] trackers = metaFile.MetaInfoDict["announce-list"];
                BitTorrent.Log.Logger.Debug(Encoding.ASCII.GetString(trackers));
            }
        }

        public static void Main(string[] args)
        {
            try
            {

                Log.Logger.Info("Loading and parsing metainfo for torrent file ....");

                MetaInfoFile torrentFile = new MetaInfoFile("/home/robt/torrent/mint.iso.torrent");

                torrentFile.Load();
                torrentFile.Parse();

                Agent agent = new Agent(torrentFile, "/home/robt/utorrent");

                if (agent.BytesLeftToDownload() != 0) {

                    Tracker tracker = new Tracker(agent.TrackerURL, agent.InfoHash, agent.UpdatePeerSwarm)
                    {
                        Left = agent.BytesLeftToDownload()
                    };
                    agent.MainTracker = tracker;

                    tracker.IntialAnnounce();
                    tracker.StartAnnouncing();

                    agent.Download();

                    agent.Close();

                } else {
                    Log.Logger.Info("Torrent has been fully downloaded.");
                }
            }
            catch (Error ex)
            {
                BitTorrent.Log.Logger.Error(ex.Message);
            }
            catch (Exception ex)
            {
                BitTorrent.Log.Logger.Error(ex);
            }
        }
    }
}
