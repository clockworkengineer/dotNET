//
// Author: Robert Tizzard
//
// Library: C# class library to implement the BitTorrent protocol.
//
// Description: Provide functionality for downloading pieces of a torrent
// from a remote server using the piece selector algorithm passed to it. If
// the remote peer chokes while a piece is being processed then the the processing
// of the piece halts and it is requeued for download; except when the piece has
// sucessfully been assembled locally when the choke occurs then it is queued for
// writing to disk.
//
// TODO: Needs better use of waits and positoning of choke checks.
//
// Copyright 2020.
//

using System;
using System.Text;
using System.Threading;

namespace BitTorrentLibrary
{

    public interface IAssembler
    {
        ManualResetEvent Paused { get; }
        void AssemblePieces(Peer remotePeer);
    }

    /// <summary>
    /// Piece Assembler
    /// </summary>
    public class Assembler : IAssembler
    {

        public ManualResetEvent Paused { get; }      // == false (unset) pause downloading from peer

        /// <summary>
        /// Queue sucessfully assembled piece for writing to disk or requeue for download if not.
        /// </summary>
        /// <param name="remotePeer"></param>
        /// <param name="pieceNumber"></param>
        /// <param name="pieceAssembled"></param>
        private void QeueAssembledPieceToDisk(Peer remotePeer, UInt32 pieceNumber, bool pieceAssembled)
        {

            if (!remotePeer.Tc.DownloadFinished.WaitOne(0))
            {
                if (pieceAssembled)
                {
                    bool pieceValid = remotePeer.Tc.CheckPieceHash(pieceNumber, remotePeer.AssembledPiece.Buffer, remotePeer.Tc.GetPieceLength(pieceNumber));
                    if (pieceValid)
                    {
                        Log.Logger.Debug($"All blocks for piece {pieceNumber} received");
                        remotePeer.Tc.PieceWriteQueue.Add(new PieceBuffer(remotePeer.AssembledPiece));
                        remotePeer.Tc.MarkPieceLocal(pieceNumber, true);
                    }
                }

                if (!remotePeer.Tc.IsPieceLocal(pieceNumber))
                {
                    Log.Logger.Debug($"REQUEUING PIECE {pieceNumber}");
                    remotePeer.Tc.MarkPieceMissing(pieceNumber, true);
                }

                remotePeer.AssembledPiece.Reset();
            }

        }
        /// <summary>
        /// Wait for event to be set throwing a cancel exception if it is fired.
        /// </summary>
        /// <param name="evt"></param>
        /// <param name="cancelTask"></param>
        private void WaitOnWithCancellation(ManualResetEvent evt, CancellationToken cancelTask)
        {
            if (WaitHandle.WaitAny(new WaitHandle[] { evt, cancelTask.WaitHandle }) == 1)
            {
                cancelTask.ThrowIfCancellationRequested();
            }
        }
        /// <summary>
        /// Request piece from remote peer. If peer is choked or an cancel arises exit without completeing
        /// requests so that piece can be requeued for handling later.
        /// </summary>
        /// <param name="remotePeer"></param>
        /// <param name="pieceNumber"></param>
        /// <param name="cancelTask"></param>
        /// <returns></returns>
        private bool GetPieceFromPeer(Peer remotePeer, uint pieceNumber, CancellationToken cancelTask)
        {
            WaitHandle[] waitHandles = new WaitHandle[] { remotePeer.WaitForPieceAssembly, cancelTask.WaitHandle };

            remotePeer.WaitForPieceAssembly.Reset();

            remotePeer.AssembledPiece.SetBlocksPresent(remotePeer.Tc.GetPieceLength(pieceNumber));

            UInt32 blockNumber = 0;
            for (; blockNumber < remotePeer.Tc.GetPieceLength(pieceNumber) / Constants.BlockSize; blockNumber++)
            {
                if (!remotePeer.PeerChoking.WaitOne(0))
                {
                    return false;
                }
                PWP.Request(remotePeer, pieceNumber, blockNumber * Constants.BlockSize, Constants.BlockSize);
            }

            if (remotePeer.Tc.GetPieceLength(pieceNumber) % Constants.BlockSize != 0)
            {
                if (!remotePeer.PeerChoking.WaitOne(0))
                {
                    return false;
                }
                PWP.Request(remotePeer, pieceNumber, blockNumber * Constants.BlockSize,
                             remotePeer.Tc.GetPieceLength(pieceNumber) % Constants.BlockSize);
            }

            switch (WaitHandle.WaitAny(waitHandles, 60000))
            {
                case 0:
                    remotePeer.WaitForPieceAssembly.Reset();
                    break;
                case WaitHandle.WaitTimeout:
                    return false;
                default:
                    cancelTask.ThrowIfCancellationRequested();
                    break;
            }

            return (remotePeer.AssembledPiece.AllBlocksThere);

        }
        /// <summary>
        /// Assembles the pieces of a torrent block by block. If a choke or cancel occurs when a piece is being handled the 
        /// piece is requeued for handling later by this or another task.
        /// </summary>
        /// <param name="remotePeer"></param>
        /// <param name="cancelTask"></param>
        private void AssembleMissingPieces(Peer remotePeer, CancellationToken cancelTask)
        {
            UInt32 nextPiece = 0;

            try
            {

                Log.Logger.Debug($"Running piece assembler for peer {Encoding.ASCII.GetString(remotePeer.RemotePeerID)}.");
                PWP.Unchoke(remotePeer);
                PWP.Interested(remotePeer);
                WaitOnWithCancellation(remotePeer.PeerChoking, cancelTask);

                while (!remotePeer.Tc.DownloadFinished.WaitOne(0))
                {
                    while (remotePeer.Tc.PieceSelector.NextPiece(remotePeer, ref nextPiece, cancelTask))
                    {
                        Log.Logger.Debug($"Assembling blocks for piece {nextPiece}.");
                        QeueAssembledPieceToDisk(remotePeer, nextPiece, GetPieceFromPeer(remotePeer, nextPiece, cancelTask));
                        WaitOnWithCancellation(remotePeer.PeerChoking, cancelTask);
                        WaitOnWithCancellation(Paused, cancelTask);
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message);
                QeueAssembledPieceToDisk(remotePeer, nextPiece, remotePeer.AssembledPiece.AllBlocksThere);
                throw;
            }

        }
        /// <summary>
        /// Loop dealing with piece requests until peer connection closed.
        /// </summary>
        /// <param name="remotePeer"></param>
        /// <param name="cancelTask"></param>
        private void ProcessRemotePeerRequests(Peer remotePeer, CancellationToken cancelTask)
        {

            try
            {
                if (remotePeer.Connected)
                {
                    if (remotePeer.NumberOfMissingPieces != 0)
                    {
                        WaitHandle[] waitHandles = new WaitHandle[] { cancelTask.WaitHandle };
                        PWP.Uninterested(remotePeer);
                        PWP.Unchoke(remotePeer);
                        WaitHandle.WaitAll(waitHandles);
                    }
                    else
                    {
                        // SHOULD ADD TO DEAD PEERS LIST HERE TO (NEED TO MOVE IT TO TC)
                        Log.Logger.Info($"Remote Peer doesn't need pieces. Closing the connection.");
                        remotePeer.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message);
                throw;
            }

        }
        /// <summary>
        /// Setup data and resources needed by assembler.
        /// </summary>
        /// <param name="torrentDownloader"></param>
        /// <param name="progressFunction"></param>
        /// <param name="progressData"></param>
        public Assembler()
        {
            Paused = new ManualResetEvent(false);
        }
        /// <summary>
        /// Task method to download any missing pieces of torrent and when that is done to simply
        /// loop processing remote peer commands until the connection is closed.
        /// </summary>
        /// <param name="remotePeer"></param>
        /// <param name="_downloadFinished"></param>
        public void AssemblePieces(Peer remotePeer)
        {

            try
            {

                CancellationToken cancelTask = remotePeer.CancelTaskSource.Token;

                WaitOnWithCancellation(remotePeer.BitfieldReceived, cancelTask);

                foreach (var pieceNumber in remotePeer.Tc.PieceSelector.LocalPieceSuggestions(remotePeer, 10))
                {
                    PWP.Have(remotePeer, pieceNumber);
                }

                if (remotePeer.Tc.BytesLeftToDownload() > 0)
                {
                    AssembleMissingPieces(remotePeer, cancelTask);
                }

                ProcessRemotePeerRequests(remotePeer, cancelTask);

            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message);
            }

            remotePeer.Close();

            Log.Logger.Debug($"Exiting block assembler for peer {Encoding.ASCII.GetString(remotePeer.RemotePeerID)}.");

        }
    }
}
