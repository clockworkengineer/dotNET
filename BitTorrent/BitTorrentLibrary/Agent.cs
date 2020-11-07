﻿//
// Author: Robert Tizzard
//
// Library: C# class library to implement the BitTorrent protocol.
//
// Description: All the high level torrent control logic including download/upload
// of torrent pieces and updating the peers in the current swarm. When a torrent context
// is added to an agent it has a piece assembler task created for it which puts together
// pieces that they request from the torrent (remote peer) before being written to disk.
//
// Copyright 2020.
//

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Sockets;

namespace BitTorrentLibrary
{

    /// <summary>
    /// Agent class definition.
    /// </summary>
    public class Agent
    {
        private readonly Manager _manager;                                 // Torrent context/ dead peer manager
        private bool _agentRunning = false;                                // == true while agent is up and running.
        private readonly Assembler _pieceAssembler;                        // Piece assembler for agent
        private Socket _listenerSocket;                                    // Connection listener socket
        private readonly CancellationTokenSource _cancelWorkerTaskSource;  // Cancel all agent worker tasks
        private readonly AsyncQueue<PeerDetails> _peerSwarmQeue;           // Queue of peers to add to swarm
        private readonly AsyncQueue<Peer> _peerCloseQueue;                 // Peer close queue

        /// <summary>
        /// Peer close processing task.
        /// <param name="cancelTask"></param>
        /// <returns></returns>
        private async Task PeerCloseQueueTaskAsync(CancellationToken cancelTask)
        {

            Log.Logger.Info("Peer close queue task started ... ");

            try
            {
                while (_agentRunning)
                {
                    Peer peer = await _peerCloseQueue.DequeueAsync(cancelTask);
                    peer.Close();
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Debug("BitTorrent (Agent) Error :" + ex.Message);
            }

            Log.Logger.Info("Peer close queue task terminated.");

        }
        /// <summary>
        /// Add Peer to swarm.
        /// </summary>
        /// <param name="remotePeer"></param>
        private void AddPeerToSwarm(Peer remotePeer)
        {

            remotePeer.Connect(_manager);

            remotePeer.peerCloseQueue = _peerCloseQueue;

            if (remotePeer.Connected)
            {
                if (!_manager.IsPeerDead(remotePeer.Ip) && remotePeer.Tc.IsSpaceInSwarm(remotePeer.Ip))
                {
                    if (remotePeer.Tc.PeerSwarm.TryAdd(remotePeer.Ip, remotePeer))
                    {
                        remotePeer.BitfieldReceived.WaitOne();
                        foreach (var pieceNumber in remotePeer.Tc.Selector.LocalPieceSuggestions(remotePeer, 10))
                        {
                            PWP.Have(remotePeer, pieceNumber);
                        }
                        PWP.Uninterested(remotePeer);
                        PWP.Unchoke(remotePeer);
                        Log.Logger.Info($"BTP: Local Peer [{ PeerID.Get()}] to remote peer [{Encoding.ASCII.GetString(remotePeer.RemotePeerID)}].");
                    }
                    else
                    {
                        remotePeer.Connected = false;
                    }
                }
            }
            else
            {
                remotePeer.Tc = null;
            }

            if ((remotePeer.Tc == null) || (!remotePeer.Connected))
            {
                remotePeer.QueueForClosure();
            }

        }
        /// <summary>
        /// Inspect  peer queue, connect to the peer and add into to swarm.
        /// </summary>
        /// <param name="cancelTask"></param>
        /// <returns></returns>
        private async Task PeerConnectCreatorTaskAsync(CancellationToken cancelTask)
        {

            Log.Logger.Info("Remote peer connect creation task started...");

            try
            {
                while (_agentRunning)
                {
                    PeerDetails peer = await _peerSwarmQeue.DequeueAsync(cancelTask);
                    try
                    {
                        if (_manager.GetTorrentContext(peer.infoHash, out TorrentContext tc))
                        {
                            AddPeerToSwarm(new Peer(peer.ip, peer.port, tc, null));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Add to dead peer here depending on exception type.
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Debug("BitTorrent (Agent) Error :" + ex.Message);
            }

            Log.Logger.Info("Remote peer connect creation task terminated.");

        }
        /// <summary>
        /// Listen for remote peer connects and on success add it to swarm.
        /// </summary>
        /// <param name="_"></param>
        /// <returns></returns>
        private async Task PeerListenCreatorTaskAsync(CancellationToken _)
        {

            Log.Logger.Info("Remote Peer connect listener started...");

            try
            {
                _listenerSocket = PeerNetwork.GetListeningConnection();

                while (_agentRunning)
                {
                    Log.Logger.Info("Waiting for remote peer connect...");

                    Socket remotePeerSocket = await PeerNetwork.WaitForConnectionAsync(_listenerSocket);

                    if (_agentRunning)
                    {
                        Log.Logger.Info("Remote peer connected...");
                        var endPoint = PeerNetwork.GetConnectionEndPoint(remotePeerSocket);
                        AddPeerToSwarm(new Peer(endPoint.Item1, endPoint.Item2, null, remotePeerSocket));
                    }

                }
            }
            catch (Exception ex)
            {
                Log.Logger.Debug("BitTorrent (Agent) Error :" + ex.Message);
            }

            _listenerSocket?.Close();

            Log.Logger.Info("Remote Peer connect listener terminated.");

        }
        /// <summary>
        /// Setup data and resources needed by agent.
        /// </summary>
        /// <param name="manager">Torrent context manager</param>
        /// <param name="downloadPath">Download path.</param>
        public Agent(Manager manager, Assembler pieceAssembler)
        {
            _manager = manager;
            _pieceAssembler = pieceAssembler;
            _peerSwarmQeue = new AsyncQueue<PeerDetails>();
            _cancelWorkerTaskSource = new CancellationTokenSource();
            _peerCloseQueue = new AsyncQueue<Peer>();
        }
        /// <summary>
        /// Startup agent.
        /// </summary>
        public void Startup()
        {
            try
            {
                Log.Logger.Info("Starting up Torrent Agent...");
                _agentRunning = true;
                Task.Run(() => Task.WaitAll(PeerConnectCreatorTaskAsync(_cancelWorkerTaskSource.Token),
                                            PeerListenCreatorTaskAsync(_cancelWorkerTaskSource.Token),
                                            PeerCloseQueueTaskAsync(_cancelWorkerTaskSource.Token)));
                Log.Logger.Info("Torrent Agent started.");
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex);
                throw new Error("BitTorrent (Agent) Error : Failure to startup agent." + ex.Message);
            }
        }
        /// <summary>
        /// Add torrent context to managers database while creating an assembler task for it.
        /// </summary>
        /// <param name="tc"></param>
        public void Add(TorrentContext tc)
        {
            try
            {

                tc.AssemblerTask = Task.Run(() => _pieceAssembler.AssemblePieces(tc, tc.CancelAssemblerTaskSource.Token));
                _manager.AddTorrentContext(tc);
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex);
                throw new Error("BitTorrent (Agent) Error : Failed to add torrent context." + ex.Message);
            }
        }
        /// <summary>
        /// Remove torrent context from managers database.
        /// </summary>
        /// <param name="tc"></param>
        public void Remove(TorrentContext tc)
        {
            try
            {
                _manager.RemoveTorrentContext(tc);
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex);
                throw new Error("BitTorrent (Agent) Error : Failed to remove torrent context. " + ex.Message);
            }
        }
        /// <summary>
        /// Shutdown any agent running resources.
        /// </summary>
        public void ShutDown()
        {
            try
            {
                if (_agentRunning)
                {
                    Log.Logger.Info("Shutting down torrent agent...");
                    _cancelWorkerTaskSource.Cancel();
                    foreach (var tc in _manager.TorrentList)
                    {
                        Close(tc);
                    }
                    PeerNetwork.ShutdownListener();
                    _agentRunning = false;
                    Log.Logger.Info("Torrent agent shutdown.");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex);
                throw new Error("BitTorrent (Agent) Error : Failed to shutdown agent." + ex.Message);
            }

        }
        /// <summary>
        /// Wait for a torrent to finish downloading.
        /// </summary>
        public void WaitForDownload(TorrentContext tc)
        {
            try
            {
                if (tc.MainTracker.Left != 0)
                {
                    Log.Logger.Info("Waiting for torrent to download for MetaInfo data ...");
                    tc.Status = TorrentStatus.Downloading;
                    tc.DownloadFinished.WaitOne();
                    tc.MainTracker.ChangeStatus(Tracker.TrackerEvent.completed);
                    Log.Logger.Info("Whole Torrent finished downloading.");
                }

                tc.Status = TorrentStatus.Seeding;

            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex);
                throw new Error("BitTorrent (Agent) Error : Failed to download torrent file.");
            }
        }
        /// <summary>
        /// Wait for torrent to download asynch.
        /// </summary>
        public async Task WaitForDownloadAsync(TorrentContext tc)
        {
            await Task.Run(() => WaitForDownload(tc)).ConfigureAwait(false);
        }
        /// <summary>
        /// Closedown Agent
        /// </summary>
        public void Close(TorrentContext tc)
        {
            try
            {
                tc.CancelAssemblerTaskSource.Cancel();
                Log.Logger.Info($"Closing torrent context for {Util.InfoHashToString(tc.InfoHash)}.");
                tc.MainTracker.StopAnnouncing();
                if (tc.PeerSwarm != null)
                {
                    Log.Logger.Info("Closing peer sockets.");
                    foreach (var remotePeer in tc.PeerSwarm.Values)
                    {
                        remotePeer.QueueForClosure();
                    }
                }
                tc.MainTracker.ChangeStatus(Tracker.TrackerEvent.stopped);
                tc.Status = TorrentStatus.Ended;
                Log.Logger.Info("Torrent context closed.");

            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex);
                throw new Error("BitTorrent (Agent) Error : Failure to close torrent context" + ex.Message);
            }
        }
        /// <summary>
        /// Start downloading torrent.
        /// </summary>
        public void Start(TorrentContext tc)
        {
            try
            {
                _pieceAssembler?.Paused.Set();
                tc.Status = TorrentStatus.Initialised;
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex);
                throw new Error("BitTorrent (Agent) Error : Failure to start torrent context." + ex.Message);
            }
        }
        /// <summary>
        /// Pause downloading torrent.
        /// </summary>
        public void Pause(TorrentContext tc)
        {
            try
            {
                _pieceAssembler?.Paused.Reset();
                tc.Status = TorrentStatus.Paused;
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex);
                throw new Error("BitTorrent (Agent) Error : Failure to pause torrent context." + ex.Message);
            }
        }
        /// <summary>
        /// Return details about the currently torrents status.
        /// </summary>
        /// <returns></returns>
        public TorrentDetails GetTorrentDetails(TorrentContext tc)
        {

            return new TorrentDetails
            {
                fileName = tc.FileName,
                status = tc.Status,

                peers = (from peer in tc.PeerSwarm.Values
                         select new PeerDetails
                         {
                             ip = peer.Ip,
                             port = peer.Port
                         }).ToList(),

                downloadedBytes = tc.TotalBytesDownloaded,
                uploadedBytes = tc.TotalBytesUploaded,
                infoHash = tc.InfoHash,
                missingPiecesCount = tc.MissingPiecesCount,
                swarmSize = (UInt32)tc.PeerSwarm.Count,
                deadPeers = (UInt32)_manager.DeadPeerCount
            };
        }
        /// <summary>
        /// Attach peer swarm queue to atart recieving peers.
        /// </summary>
        /// <param name="tracker"></param>
        public void AttachPeerSwarmQueue(Tracker tracker)
        {
            tracker._peerSwarmQueue = _peerSwarmQeue;
        }
        /// <summary>
        /// Detach peer swarm than queue.
        /// </summary>
        /// <param name="tracker"></param>
        public void DetachPeerSwarmQueu(Tracker tracker)
        {
            tracker._peerSwarmQueue = null;
        }
    }
}
