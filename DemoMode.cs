using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Services;

namespace KillerNotes
{
    // Screenshot / demo mode. Launch with `KillerNotes.exe --demo` (or /demo). A scratch
    // database (demo-notes.db) is recreated on every demo launch and filled with the
    // fabricated notes below; the real notes.db is never opened. Only reachable through
    // the launch flag - no button, safe to leave in a shipped build (KillerScan pattern).
    //
    // Everything here is invented. Client names are fictional and every address uses the
    // TEST-NET documentation ranges, so nothing matches any real environment.
    public partial class MainWindow
    {
        public static bool DemoMode;

        // Demo tags: MSP-flavored named tags (order sets the Ctrl+1..6 slots) that replace
        // the auto-seeded color-named defaults, so screenshots show real categories.
        private static readonly (string Name, string Color)[] DemoTags =
        [
            ("On-site",           "#50AEE8"),
            ("Urgent",            "#DD504B"),
            ("Follow-up",         "#E8962C"),
            ("Network",           "#B982E3"),
            ("Reference",         "#1EA54C"),
            ("Waiting on vendor", "#E8D44B"),
        ];

        private void GenerateDemoNotes()
        {
            if (!NoteStore.IsOpen) return;
            var now = DateTime.Now;
            long showcase = -1;

            void Add(string title, double daysAgo, FlowDocument doc, bool feature = false, string? tags = null, string? group = null, string? titleColor = null)
            {
                long id = CreateNoteFromDocument(title, doc);   // ImportExport.cs
                var created = now.AddDays(-daysAgo);
                var modified = created.AddHours(2 + (daysAgo % 5) * 7);
                if (modified > now) modified = now.AddMinutes(-14);
                NoteStore.SetTimestamps(id, created, modified);
                if (tags != null) NoteStore.SetNoteTags(id, tags);
                if (group != null) NoteStore.SetNoteGroup(id, group);
                if (titleColor != null) NoteStore.SetTitleColor(id, titleColor);
                if (feature) showcase = id;
            }

            // Swap the color-named defaults for the MSP-flavored set, then tag the notes.
            foreach (var t in NoteStore.ListTags()) NoteStore.DeleteTag(t.Name);
            foreach (var t in DemoTags) NoteStore.AddTag(t.Name, t.Color);

            // Family accent palette, reused across group colors + a few note titles.
            const string BLUE = "#50AEE8", RED = "#DD504B", ORANGE = "#E8962C", PURPLE = "#B982E3",
                         GREEN = "#1EA54C", YELLOW = "#E8D44B", TEAL = "#2BB6A3", PINK = "#E86FA6",
                         INDIGO = "#6A6AE3", SLATE = "#7A8CA3";

            // Build a nested group PATH from its parts ("A","B" => A<sep>B).
            string P(params string[] parts)
            {
                string p = "";
                foreach (var s in parts) p = NoteStore.GroupPath(p, s);
                return p;
            }
            // Create a (sub)group at parts and color it. Parents are created first (calls run in
            // pre-order), which also sets the top-to-bottom sidebar order.
            void G(string color, params string[] parts)
            {
                string path = P(parts);
                string parent = NoteStore.GroupParentOf(path);   // net48 has no array-range slicing
                NoteStore.AddGroup(path, parent, atTop: false);   // pre-order build => keep top-to-bottom order
                NoteStore.SetGroupColor(path, color);
            }

            // A deep, colorful tree so the demo shows groups, nested subgroups (up to three
            // levels), and per-group colors. A few notes stay ungrouped as the loose tail (#8).
            G(BLUE,   "Client sites");
            G(TEAL,   "Client sites", "Northwind Dental");
            G(RED,    "Client sites", "Meadowbrook Vet");
            G(INDIGO, "Client sites", "Oakfield Law");
            G(ORANGE, "Client sites", "Oakfield Law", "Phase 2");
            G(PURPLE, "Bench reference");
            G(GREEN,  "Bench reference", "PowerShell");
            G(ORANGE, "Bench reference", "Networking");
            G(BLUE,   "Bench reference", "Networking", "VLAN cheatsheets");
            G(YELLOW, "Bench reference", "Hardware");
            G(PINK,   "Projects");
            G(RED,    "Projects", "Firewall refresh");
            G(BLUE,   "Projects", "Wi-Fi survey");
            G(SLATE,  "Admin");

            // ---- Client sites ----
            Add("Northwind Dental - site visit", 38, DemoSiteVisit(), tags: "On-site, Reference", group: P("Client sites", "Northwind Dental"));
            Add("Imaging server quirk", 35, DemoDoc("The bench imaging box drops its second NIC after a reboot. Disable and re-enable it, or just leave NIC1 patched.",
                "PXE only works on NIC1", "Static 192.0.2.40 /24 on the imaging VLAN", "Amber light is a bad LED, not the PSU"),
                tags: "Reference", group: P("Client sites", "Northwind Dental"), titleColor: TEAL);
            Add("After-hours contacts", 30, DemoDoc("Escalation for the main office, in order:",
                "Office manager - has the alarm code", "Practice owner - text first, never call after 21:00", "Alarm company passphrase is on the work order"),
                tags: "Reference", group: P("Client sites", "Northwind Dental"));

            Add("Firewall swap - Meadowbrook Vet", 31, DemoFirewallSwap(), feature: true, tags: "On-site, Network", group: P("Client sites", "Meadowbrook Vet"), titleColor: RED);
            Add("Kennel cams offline", 26, DemoDoc("Four PoE cameras in the kennel keep dropping. Suspect the cheap unmanaged switch back there.",
                "Swap in the spare PoE+ switch from the van", "Camera VLAN 40, DHCP off, static .50-.70", "If they still drop it is the long run near the compressor"),
                tags: "Urgent, Follow-up", group: P("Client sites", "Meadowbrook Vet"));
            Add("Printer mapping", 22, DemoDoc("Shared printers by room, for the deploy script:",
                "Front desk - HP M428 (192.0.2.61)", "Lab - Brother HL-L2350 (192.0.2.62)", "Back office Lexmark - do not map, they want it gone"),
                tags: "Reference", group: P("Client sites", "Meadowbrook Vet"));

            Add("Switch port map - Oakfield Law", 20, DemoPortMap(), tags: "Network, Reference", group: P("Client sites", "Oakfield Law"));
            Add("VPN user list", 18, DemoDoc("Who has client VPN and why. Review quarterly.",
                "3 partners - always on", "2 paralegals - remote days only", "1 vendor account - disable when the case closes"),
                tags: "Reference", group: P("Client sites", "Oakfield Law"));

            Add("Phase 2 - cabling scope", 15, DemoDoc("Second-floor buildout. Rough count before the quote:",
                "14 new drops, all Cat6", "2 WAPs at the hallway ends", "Home-run to the second-floor IDF, not the MDF"),
                tags: "On-site", group: P("Client sites", "Oakfield Law", "Phase 2"));
            Add("Phase 2 - cutover runbook", 13, DemoDoc("Order of operations for the cutover weekend:",
                "Label and test every new drop Friday PM", "Move users desk by desk Saturday", "Old IDF stays live one week as rollback"),
                tags: "Follow-up", group: P("Client sites", "Oakfield Law", "Phase 2"), titleColor: ORANGE);

            // ---- Bench reference ----
            Add("PowerShell one-liners", 27, DemoPowerShell(), tags: "Reference", group: P("Bench reference", "PowerShell"));
            Add("Bulk AD password reset", 24, DemoDocMono("Force a reset at next logon for a whole OU:",
                "Get-ADUser -Filter * -SearchBase 'OU=Staff,DC=corp,DC=local' | Set-ADUser -ChangePasswordAtLogon $true",
                "Skip the service accounts - they live in OU=Service", "Hand out the temp passwords out of band"),
                tags: "Reference", group: P("Bench reference", "PowerShell"));
            Add("Export mailbox sizes", 19, DemoDocMono("Quick capacity check before a migration:",
                "Get-MailboxStatistics -Server EX01 | Sort TotalItemSize -Desc | Select DisplayName,TotalItemSize"),
                tags: "Reference", group: P("Bench reference", "PowerShell"));

            Add("Subnet quick math", 21, DemoSubnet(), tags: "Reference", group: P("Bench reference", "Networking"));
            Add("DNS troubleshooting order", 17, DemoDoc("When name resolution is flaky, work it in this order:",
                "flushdns, then nslookup against the server directly", "Check the forwarders on the DNS server, not just the client",
                "Confirm the client points at the internal DNS, not the router", "Only then suspect the record itself"),
                tags: "Reference", group: P("Bench reference", "Networking"));

            Add("VLAN numbering standard", 14, DemoVlan(), tags: "Reference", group: P("Bench reference", "Networking", "VLAN cheatsheets"), titleColor: BLUE);
            Add("Trunk config snippets", 11, DemoDocMono("The uplink trunk I paste on every access switch:",
                "switchport mode trunk ; switchport trunk allowed vlan 10,20,30,40,99", "Native VLAN 1, unused everywhere"),
                tags: "Reference", group: P("Bench reference", "Networking", "VLAN cheatsheets"));

            Add("UPS runtimes", 16, DemoUps(), tags: "Reference, Follow-up", group: P("Bench reference", "Hardware"));
            Add("Parts drawer inventory", 5, DemoParts(), tags: "Reference", group: P("Bench reference", "Hardware"));
            Add("Drive shucking notes", 9, DemoDoc("Cheap external drives for the backup rotation:",
                "Tape over the 3.3V pin or the drive will not spin in the NAS", "8TB+ white-labels are usually CMR, but test",
                "Log the serial before shucking - warranty voids"),
                tags: "Reference", group: P("Bench reference", "Hardware"));

            // ---- Projects ----
            Add("Firewall refresh - vendor quotes", 12, DemoDoc("Comparing the two firewall vendors for the fleet refresh. Waiting on the second quote.",
                "Vendor A - cheaper box, pricier licensing", "Vendor B - better throughput, 3-year bundle", "Both cover the VLANs and the site-to-site we need"),
                tags: "Waiting on vendor", group: P("Projects", "Firewall refresh"));
            Add("Firewall refresh - migration plan", 10, DemoDoc("One site per weekend, lowest-risk first:",
                "Start with the single-VPN sites", "Multi-peer sites last, once the runbook is solid", "Keep each old unit racked a week as rollback"),
                tags: "Follow-up", group: P("Projects", "Firewall refresh"), titleColor: RED);

            Add("Wi-Fi survey - AP placement", 8, DemoDoc("Walk-through notes for the warehouse survey:",
                "Dead spot at the far loading dock - needs its own AP", "Office side is fine on 2 APs", "Metal racking kills 5GHz down the aisles"),
                tags: "On-site", group: P("Projects", "Wi-Fi survey"));
            Add("Wi-Fi survey - channel plan", 7, DemoDoc("Non-overlapping channel plan after the survey:",
                "2.4GHz - 1, 6, 11 only, never auto", "5GHz - let the controller pick but cap TX power", "Neighboring tenant sits on 6, keep our high-density APs off it"),
                tags: "Reference", group: P("Projects", "Wi-Fi survey"));

            // ---- Admin (notes filed directly in the group, no subgroups) ----
            Add("New tech onboarding", 12, DemoOnboarding(), tags: "Reference", group: P("Admin"));
            Add("On-call rotation", 6, DemoOnCall(), tags: "Reference", group: P("Admin"));
            Add("Expense receipts - reminder", 4, DemoDoc("Submit receipts by the last business day or they roll to next month.",
                "Photograph the receipt at the counter, do not save it for later", "Mileage log is in the shared drive", "Personal-card parts need the PO number in the memo"),
                tags: "Follow-up", group: P("Admin"), titleColor: SLATE);

            // ---- Ungrouped tail ----
            Add("RMM agent cleanup", 8, DemoRmm(), tags: "Follow-up", titleColor: ORANGE);
            Add("Callback list", 1, DemoDoc("Loose ends to chase tomorrow:",
                "NAS quote - they want the 4-bay after all", "Ticket #4183 still waiting on the ISP", "Return the loaner laptop to the vet office"),
                tags: "Follow-up");
            Add("Scratch", 0.05, DemoScratch(), tags: "Urgent, Waiting on vendor");

            SearchBox.Text = "";
            RefreshList();
            if (showcase >= 0)
            {
                OpenNote(showcase);
                _syncingSelection = true;
                NotesList.SelectedItem = _notes.Find(n => n.Id == showcase);
                _syncingSelection = false;
            }
            StatusText.Text = $"{_notes.Count} notes";
        }

        // ---- Small builders (concrete brushes on purpose: XamlPackage blobs cannot
        //      keep theme-reactive references, same rule as the horizontal-rule insert) ----

        private static Paragraph DemoP(string text, bool bold = false, string? color = null)
        {
            var run = new Run(text);
            if (bold) run.FontWeight = FontWeights.Bold;
            if (color != null) run.Foreground =
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            return new Paragraph(run);
        }

        private static Paragraph DemoMono(string text)
        {
            var p = new Paragraph(new Run(text)) { FontFamily = new FontFamily("Consolas") };
            return p;
        }

        // Compact builders for the many short demo notes: an intro paragraph plus an optional
        // bullet list (DemoDoc) or a mono command line between them (DemoDocMono).
        private static FlowDocument DemoDoc(string intro, params string[] bullets)
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP(intro));
            if (bullets.Length > 0) d.Blocks.Add(DemoList(bullets));
            return d;
        }

        private static FlowDocument DemoDocMono(string intro, string mono, params string[] bullets)
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP(intro));
            d.Blocks.Add(DemoMono(mono));
            if (bullets.Length > 0) d.Blocks.Add(DemoList(bullets));
            return d;
        }

        private static List DemoList(params string[] items)
        {
            var list = new List { MarkerStyle = TextMarkerStyle.Disc };
            foreach (var i in items) list.ListItems.Add(new ListItem(new Paragraph(new Run(i))));
            return list;
        }

        private static Paragraph DemoRule() => new()
        {
            FontSize = 2,
            Margin = new Thickness(0, 8, 0, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5a, 0x5a, 0x5a)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        private static Table DemoTable(string[] header, params string[][] rows)
        {
            var border = new SolidColorBrush(Color.FromRgb(0x5a, 0x5a, 0x5a));
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 6), BorderBrush = border, BorderThickness = new Thickness(1, 1, 0, 0) };
            for (int c = 0; c < header.Length; c++) table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            var head = new TableRow();
            foreach (var h in header)
            {
                var run = new Run(h) { FontWeight = FontWeights.Bold };
                head.Cells.Add(new TableCell(new Paragraph(run))
                { BorderBrush = border, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(6, 3, 6, 3) });
            }
            group.Rows.Add(head);
            foreach (var row in rows)
            {
                var tr = new TableRow();
                foreach (var cell in row)
                    tr.Cells.Add(new TableCell(new Paragraph(new Run(cell)))
                    { BorderBrush = border, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(6, 3, 6, 3) });
                group.Rows.Add(tr);
            }
            table.RowGroups.Add(group);
            return table;
        }

        // ---- The notes ----

        private static FlowDocument DemoSiteVisit()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Quarterly maintenance, main office. On-site window 8:00-12:00.", bold: false));
            d.Blocks.Add(DemoList(
                "Replace UPS batteries in the MDF (2x RBC115, in the van)",
                "Firmware on both switches - approved by office manager",
                "Check backup job history - NAS reported 2 warnings last week",
                "Label the new drops in suite 210",
                "Grab a photo of the patch panel BEFORE touching anything"));
            d.Blocks.Add(DemoRule());
            d.Blocks.Add(DemoP("Gate code is on the work order. Park behind the building.", color: "#c9a227"));
            return d;
        }

        private static FlowDocument DemoFirewallSwap()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Cutover scheduled Friday 17:30. Old unit stays racked for one week as rollback.", bold: true));
            d.Blocks.Add(DemoTable(
                new[] { "Setting", "Old unit", "New unit" },
                new[] { "WAN IP", "203.0.113.10 /29", "203.0.113.10 /29" },
                new[] { "LAN GW", "192.0.2.1 /24", "192.0.2.1 /24" },
                new[] { "VPN peers", "3 (see vendor sheet)", "re-key all 3" },
                new[] { "DNS fwd", "198.51.100.53", "198.51.100.53" },
                new[] { "Mgmt access", "LAN only", "LAN + mgmt VLAN 99" }));
            d.Blocks.Add(DemoP("Port-forward list exported and attached to the ticket. Test plan:"));
            d.Blocks.Add(DemoList(
                "VPN up from all 3 peers",
                "Phones re-register (SIP ALG stays OFF)",
                "Guest Wi-Fi isolated from LAN",
                "Speed test before/after"));
            return d;
        }

        private static FlowDocument DemoPowerShell()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("The ones I always forget:"));
            d.Blocks.Add(DemoMono("Get-WinEvent -FilterHashtable @{LogName='System';Level=2} -MaxEvents 25"));
            d.Blocks.Add(DemoMono("Test-NetConnection 192.0.2.20 -Port 3389"));
            d.Blocks.Add(DemoMono("Get-Volume; Get-Disk | Sort Number"));
            d.Blocks.Add(DemoMono("gpupdate /force; gpresult /h C:\\temp\\gp.html"));
            d.Blocks.Add(DemoRule());
            d.Blocks.Add(DemoP("All PS 5.1-safe. Keep them one line for LiveConnect.", color: "#3f9b56"));
            return d;
        }

        private static FlowDocument DemoPortMap()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoTable(
                new[] { "Port", "VLAN", "Goes to" },
                new[] { "1-8", "10", "Workstations, front office" },
                new[] { "9-12", "10", "Workstations, paralegals" },
                new[] { "13-16", "20", "Phones" },
                new[] { "17-18", "30", "Printers" },
                new[] { "19-22", "10", "Conference rooms" },
                new[] { "23", "99", "AP uplink (trunk)" },
                new[] { "24", "trunk", "Uplink to firewall" }));
            d.Blocks.Add(DemoP("Spare drops in the ceiling above suite B - unterminated."));
            return d;
        }

        private static FlowDocument DemoUps()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Measured on battery with normal load, this quarter:"));
            d.Blocks.Add(DemoTable(
                new[] { "Location", "Model", "Runtime" },
                new[] { "MDF", "1500VA rack", "22 min" },
                new[] { "Front desk", "650VA tower", "9 min" },
                new[] { "Server closet", "3000VA rack", "41 min" }));
            d.Blocks.Add(DemoRule());
            d.Blocks.Add(DemoP("Front desk unit beeps under load - batteries due next visit.", bold: true, color: "#c94f4f"));
            return d;
        }

        private static FlowDocument DemoOnboarding()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Day-one checklist for a new bench tech:"));
            d.Blocks.Add(DemoList(
                "RMM console account + MFA",
                "PSA / ticketing login, assign to triage queue",
                "Bench image USB (kept in the top drawer, re-image monthly)",
                "Label maker tape - we standardize on 12mm",
                "Read the escalation matrix BEFORE the first after-hours call"));
            d.Blocks.Add(DemoP("Shadow a senior on the first two site visits. No exceptions.", bold: true));
            return d;
        }

        private static FlowDocument DemoRmm()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Offboarded client still had 14 agents checking in. Cleanup order matters:"));
            d.Blocks.Add(DemoList(
                "Disable alerting for the site FIRST (or the queue floods)",
                "Uninstall via the console job, verify service is gone",
                "Remove the site from patching policy",
                "Archive the site, do not delete - audit trail stays"));
            d.Blocks.Add(DemoP("Leftover agents show as stale after 30 days - recheck next month."));
            return d;
        }

        private static FlowDocument DemoParts()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoTable(
                new[] { "Part", "Qty", "Reorder at" },
                new[] { "Cat6 patch 1m", "18", "10" },
                new[] { "Cat6 patch 3m", "7", "5" },
                new[] { "SFP+ DAC 3m", "4", "2" },
                new[] { "RJ45 ends (bag)", "2", "1" },
                new[] { "PSU tester", "1", "-" }));
            return d;
        }

        private static FlowDocument DemoSubnet()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("The mask-to-hosts table I never keep in my head:"));
            d.Blocks.Add(DemoTable(
                new[] { "CIDR", "Mask", "Usable hosts" },
                new[] { "/24", "255.255.255.0", "254" },
                new[] { "/25", "255.255.255.128", "126" },
                new[] { "/26", "255.255.255.192", "62" },
                new[] { "/27", "255.255.255.224", "30" },
                new[] { "/28", "255.255.255.240", "14" },
                new[] { "/30", "255.255.255.252", "2" }));
            return d;
        }

        private static FlowDocument DemoVlan()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Standard VLAN numbering we use at every site:"));
            d.Blocks.Add(DemoTable(
                new[] { "VLAN", "Use", "Subnet" },
                new[] { "10", "Workstations", "192.0.2.0 /24" },
                new[] { "20", "Phones", "198.51.100.0 /24" },
                new[] { "30", "Printers", "203.0.113.0 /27" },
                new[] { "40", "Cameras / IoT", "203.0.113.32 /27" },
                new[] { "99", "Management", "203.0.113.240 /28" }));
            d.Blocks.Add(DemoP("Keep cameras and IoT off the workstation VLAN, always.", bold: true, color: "#3f9b56"));
            return d;
        }

        private static FlowDocument DemoOnCall()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("Rotation runs Monday to Monday. Swap with whoever, just update the calendar."));
            d.Blocks.Add(DemoTable(
                new[] { "Week", "Primary", "Backup" },
                new[] { "This week", "Me", "Priya" },
                new[] { "Next week", "Dev", "Me" },
                new[] { "Week after", "Priya", "Dev" }));
            d.Blocks.Add(DemoP("After-hours calls go to the on-call phone, not personal numbers."));
            return d;
        }

        private static FlowDocument DemoScratch()
        {
            var d = new FlowDocument();
            d.Blocks.Add(DemoP("call back about the NAS quote - they want the 4-bay after all"));
            d.Blocks.Add(DemoP("ticket #4183 waiting on ISP"));
            return d;
        }
    }
}
