namespace KillerNotes.Features
{
    /// <summary>
    /// What AboutController needs from the window hosting it, beyond the shared shell services.
    /// Every member is a value, not a control, so the controller holds no reference to a TextBlock
    /// or a Button and can be driven by a stub in a test.
    /// </summary>
    internal interface IAboutHost : IShellServices
    {
        /// <summary>Version line, clickable through to that release.</summary>
        string Version { set; }

        /// <summary>Release date, shown muted opposite the version.</summary>
        string ReleaseDate { set; }

        /// <summary>Code-signing subject, or the unsigned message.</summary>
        string Publisher { set; }

        /// <summary>The quoted alias line, and whether the signature earned the right to show it.</summary>
        string Alias { set; }
        bool AliasVisible { set; }

        /// <summary>Certificate thumbprint.</summary>
        string Thumbprint { set; }

        /// <summary>SHA-256 of the running exe, filled in once it has been computed.</summary>
        string Sha256 { set; }

        /// <summary>The line above the update button.</summary>
        string UpdateText { set; }

        /// <summary>The update button appears only when a newer release exists, and goes insensitive
        /// while a download is running.</summary>
        bool UpdateVisible { set; }
        bool UpdateEnabled { set; }

        /// <summary>True under --demo, where the card is rendered in its signed state so marketing
        /// captures from an unsigned local build match the release (DemoMode.cs).</summary>
        bool DemoPreview { get; }

        /// <summary>Fades the About card in.</summary>
        void ShowCard();
    }
}
