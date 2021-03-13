namespace TACT.Net.FileLookup
{
    public interface IFileLookup
    {
        bool IsLoaded { get; }

        /// <summary>
        /// Opens the FileLookup and loads its contents
        /// </summary>
        void Open();

        /// <summary>
        /// Saves the FileLookup to it's backing storage
        /// </summary>
        void Close();

        uint GetOrCreateFileId(string filename);

        uint? GetOrCreateFileId(string path, bool createFileId = true);
        
        uint? Get(string path);

        string? Get(uint fileId);
    }
}
