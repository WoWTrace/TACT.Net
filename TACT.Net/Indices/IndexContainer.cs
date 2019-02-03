﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using TACT.Net.BlockTable;
using TACT.Net.Common;
using TACT.Net.Cryptography;
using TACT.Net.SystemFiles;

namespace TACT.Net.Indices
{
    public class IndexContainer : ISystemFile
    {
        public IEnumerable<IndexFile> DataIndices
        {
            get => _indices.Where(x => (x.Type & IndexType.Data) == IndexType.Data);
        }
        public IEnumerable<IndexFile> LooseIndices
        {
            get => _indices.Where(x => (x.Type & IndexType.Loose) == IndexType.Loose);
        }
        public IEnumerable<IndexFile> PatchIndices
        {
            get => _indices.Where(x => (x.Type & IndexType.Patch) == IndexType.Patch);
        }

        public MD5Hash Checksum { get; }

        private const long ArchiveDataSize = 256000000;

        /// <summary>
        /// Files enqueued to be added to a new archive
        /// </summary>
        private readonly SortedDictionary<MD5Hash, CASRecord> _fileQueue;

        private List<IndexFile> _indices;
        private string _sourceDirectory;

        #region Constructors

        public IndexContainer()
        {
            _indices = new List<IndexFile>();
            _fileQueue = new SortedDictionary<MD5Hash, CASRecord>(new HashComparer());
        }

        #endregion

        #region Methods

        /// <summary>
        /// Parses all Index files in the provided directory
        /// </summary>
        /// <param name="directory"></param>
        public void Open(string directory, bool useParallelism = true)
        {
            _sourceDirectory = directory;

            var indices = Directory.EnumerateFiles(directory, "*.index", SearchOption.AllDirectories);
            foreach (var index in indices)
                _indices.Add(new IndexFile(index));
        }

        /// <summary>
        /// Updates modified data indices and writes enqueued files to archives
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="dispose">Delete old files</param>
        /// <param name="configContainer"></param>
        public void Save(string directory, bool dispose = false, Configs.ConfigContainer configContainer = null)
        {
            // save altered Data archive indices
            foreach (var index in DataIndices)
            {
                if (!index.IsGroupIndex)
                {
                    string prevBlob = Helpers.GetCDNPath(index.Checksum.ToString(), "data", _sourceDirectory);
                    index.Write(directory, configContainer);
                    index.WriteBlob(directory, prevBlob);

                    // remove old archives
                    if (dispose)
                    {
                        Helpers.Delete(prevBlob);
                        Helpers.Delete(prevBlob + ".index");
                    }
                }
            }

            // create any new archive indices
            var partitions = EnumerablePartitioner.ConcreteBatch(_fileQueue.Values, ArchiveDataSize, (x) => x.EBlock.CompressedSize);
            foreach (var entries in partitions)
            {
                IndexFile index = new IndexFile();
                index.Add(entries);
                index.Write(directory, configContainer);
                index.WriteBlob(directory);
            }

            // reload indices
            _indices.Clear();
            Open(directory);
        }


        /// <summary>
        /// Enqueues a CASRecord for storing archiving
        /// </summary>
        /// <param name="record"></param>
        public void Enqueue(CASRecord record)
        {
            _fileQueue.TryAdd(record.EKey, record);
        }

        /// <summary>
        /// Enqueues multiple CASRecords for archiving
        /// </summary>
        /// <param name="records"></param>
        public void Enqueue(IEnumerable<CASRecord> records)
        {
            foreach (var record in records)
                Enqueue(record);
        }


        /// <summary>
        /// Opens a file stream to a file stored in the archives
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="ispatch"></param>
        /// <returns></returns>
        public BlockTableStreamReader OpenFile(MD5Hash hash, bool ispatch = false)
        {
            string directory = ispatch ? "patch" : "data";

            var enumerable = ispatch ? PatchIndices : DataIndices;
            foreach (var index in enumerable)
            {
                if (!index.IsGroupIndex && index.TryGet(hash, out var indexEntry))
                {
                    // blob file location
                    string blobpath = Helpers.GetCDNPath(index.Checksum.ToString(), directory, _sourceDirectory);

                    if (File.Exists(blobpath))
                    {
                        // open a shared stream, set the offset and return a new BLTE reader
                        var fs = new FileStream(blobpath, FileMode.Open, FileAccess.Read, FileShare.Read)
                        {
                            Position = indexEntry.Offset
                        };
                        return new BlockTableStreamReader(fs);
                    }

                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns an IndexEntry from the collection if it exists
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="indexEntry"></param>
        /// <returns></returns>
        public bool TryGet(MD5Hash hash, out IndexEntry indexEntry)
        {
            indexEntry = null;
            foreach (var index in _indices)
                if (!index.IsGroupIndex && index.TryGet(hash, out indexEntry))
                    return true;

            return false;
        }

        /// <summary>
        /// Removes an IndexFile from the collection
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public bool Remove(IndexFile index) => _indices.Remove(index);

        #endregion
    }
}
