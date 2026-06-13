using Content.Shared.Preferences;

namespace Content.Client.Lobby
{
    public interface IClientPreferencesManager
    {
        event Action OnServerDataLoaded;

        bool ServerDataLoaded => Settings != null;

        GameSettings? Settings { get; }
        PlayerPreferences? Preferences { get; }
        void Initialize();
        void SelectCharacter(ICharacterProfile profile);
        void SelectCharacter(int slot);
        void UpdateCharacter(ICharacterProfile profile, int slot);

        // Exodus-Start
        /// <summary>
        /// Updates a character in the local cache only, without sending it to the server.
        /// Use when the server is already the source of the change (e.g. a savings transfer) to avoid
        /// a redundant save that races with the server's own write.
        /// </summary>
        void UpdateCharacterLocal(ICharacterProfile profile, int slot);
        // Exodus-End
        void CreateCharacter(ICharacterProfile profile);
        void DeleteCharacter(ICharacterProfile profile);
        void DeleteCharacter(int slot);
    }
}
