//
// Author: Rob Tizzard
//
// Library: C# class library to implement the BitTorrent protocol.
//
// Description: Perform HTTP announce requests to remote tracker.
//
// Copyright 2020.
//
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
namespace BitTorrentLibrary
{
    internal class AnnouncerHTTP : IAnnouncer
    {
        private readonly IWeb _web;             // Web Layer
        private readonly Bencode _Bencode;      // Bencode encode/decode
        /// <summary>
        /// Decodes the announce request BEncoded response recieved from a tracker.
        /// </summary>
        /// <param name="announceResponse">Announce response.</param>
        /// <param name="decodedResponse">Response.</param>
        private void DecodeAnnounceResponse(Tracker tracker, byte[] announceResponse, ref AnnounceResponse decodedResponse)
        {
            if (announceResponse.Length != 0)
            {
                BNodeBase decodedAnnounce = _Bencode.Decode(announceResponse);
                decodedResponse.statusMessage = _Bencode.GetDictionaryEntryString(decodedAnnounce, "failure reason");
                if (decodedResponse.statusMessage != "")
                {
                    decodedResponse.failure = true;
                    return; // If failure present then ignore rest of reply.
                }
                int.TryParse(_Bencode.GetDictionaryEntryString(decodedAnnounce, "complete"), out decodedResponse.complete);
                int.TryParse(_Bencode.GetDictionaryEntryString(decodedAnnounce, "incomplete"), out decodedResponse.incomplete);
                BNodeBase field = _Bencode.GetDictionaryEntry(decodedAnnounce, "peers");
                if (field != null)
                {
                    decodedResponse.peerList = new List<PeerDetails>();
                    if (field is BNodeString bNodeString) // Compact peer list reply
                    {
                        decodedResponse.peerList = tracker.GetCompactPeerList( (bNodeString).str, 0);
                    }
                    else if (field is BNodeList bNodeList)  // Non-compact peer list reply
                    {
                        foreach (var listItem in (bNodeList).list)
                        {
                            if (listItem is BNodeDictionary bNodeDictionary)
                            {
                                PeerDetails peer = new PeerDetails
                                {
                                    infoHash = tracker.InfoHash
                                };
                                BNodeBase peerDictionaryItem = (bNodeDictionary);
                                BNodeBase peerField = _Bencode.GetDictionaryEntry(peerDictionaryItem, "ip");
                                if (peerField != null)
                                {
                                    peer.ip = Encoding.ASCII.GetString(((BitTorrentLibrary.BNodeString)peerField).str);
                                }
                                if (peer.ip.Contains(":"))
                                {
                                    peer.ip = peer.ip.Substring(peer.ip.LastIndexOf(":", StringComparison.Ordinal) + 1);
                                }
                                peerField = _Bencode.GetDictionaryEntry(peerDictionaryItem, "port");
                                if (peerField != null)
                                {
                                    peer.port = int.Parse(Encoding.ASCII.GetString(((BitTorrentLibrary.BNodeNumber)peerField).number));
                                }
                                if (peer.ip != tracker.Ip) // Ignore self in peers list
                                {
                                    Log.Logger.Trace($"(Tracker) Peer {peer.ip} Port {peer.port} found.");
                                    decodedResponse.peerList.Add(peer);
                                }
                            }
                        }
                    }
                }
                int.TryParse(_Bencode.GetDictionaryEntryString(decodedAnnounce, "interval"), out decodedResponse.interval);
                int.TryParse(_Bencode.GetDictionaryEntryString(decodedAnnounce, "min interval"), out decodedResponse.minInterval);
                decodedResponse.trackerID = _Bencode.GetDictionaryEntryString(decodedAnnounce, "tracker id");
                decodedResponse.statusMessage = _Bencode.GetDictionaryEntryString(decodedAnnounce, "warning message");
                decodedResponse.announceCount++;
            }
        }
        /// <summary>
        /// Build url string used for announce.
        /// </summary>
        /// <param name="tracker"></param>
        /// <returns></returns>
        private string BuildAnnouceURL(Tracker tracker)
        {
            string announceURL = $"{tracker.TrackerURL}?info_hash=" +
                  $"{Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(tracker.InfoHash, 0, tracker.InfoHash.Length))}" +
                  $"&peer_id={tracker.PeerID}&port={tracker.Port}&compact={tracker.Compact}" +
                  $"&no_peer_id={tracker.NoPeerID}&uploaded={tracker.Uploaded}&downloaded={tracker.Downloaded}" +
                  $"&left={tracker.Left}&ip={tracker.Ip}&key={tracker.Key}&trackerid={tracker.TrackerID}&numwanted={tracker.NumWanted}";
            // Some trackers require no event present if its value is none
            if (tracker.Event != TrackerEvent.None)
            {
                announceURL += $"&event={Tracker.EventString[(int)tracker.Event]}";
            }
            return announceURL;
        }
        /// <summary>
        /// Setup data and resources needed by HTTP tracker.
        /// </summary>
        /// <param name="trackerURL"></param>
        public AnnouncerHTTP(string _, IWeb web)
        {
            _web = web;
            _Bencode = new Bencode();
        }
        /// <summary>
        /// Perform an HTTP announce request to tracker and return any response.
        /// </summary>
        /// <param name="tracker"></param>
        /// <returns>Announce response.</returns>
        public AnnounceResponse Announce(Tracker tracker)
        {
            Tracker.LogAnnouce(tracker);
            AnnounceResponse response = new AnnounceResponse();
            try
            {
                _web.SetURL (BuildAnnouceURL(tracker));
                if (_web.Get())
                {
                    DecodeAnnounceResponse(tracker, _web.ResponseData, ref response);
                }
                else
                {
                    throw new BitTorrentException(_web.StatusDescription);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex);
                throw;
            }
            return response;
        }
    }
}
