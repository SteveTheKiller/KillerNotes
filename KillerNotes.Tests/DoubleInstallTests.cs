using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>KillerNotes keeps one installation per account. The all-users copy in Program Files
    /// wins over a per-user copy, so nothing here ever needs to elevate a per-user exe.</summary>
    public sealed class DoubleInstallTests
    {
        private const string UserExe = @"C:\Users\x\AppData\Local\Programs\KillerNotes\KillerNotes.exe";
        private const string MachineExe = @"C:\Program Files\KillerNotes\KillerNotes.exe";
        private const string PortableExe = @"C:\Users\x\Downloads\KillerNotes.exe";

        [Fact]
        public void APerUserInstallOpensTheAllUsersCopyInsteadOfAddingASecondOne() =>
            Assert.Equal(App.InstallPlan.OpenAllUsersCopy, App.PlanInstall(machine: false, userCopyExists: false, machineCopyExists: true));

        [Fact]
        public void APerUserInstallWithNoAllUsersCopyInstalls() =>
            Assert.Equal(App.InstallPlan.Install, App.PlanInstall(machine: false, userCopyExists: true, machineCopyExists: false));

        [Fact]
        public void AnAllUsersInstallRemovesThisAccountsPerUserCopy() =>
            Assert.Equal(App.InstallPlan.InstallThenRemoveUserCopy, App.PlanInstall(machine: true, userCopyExists: true, machineCopyExists: false));

        [Fact]
        public void AnAllUsersInstallWithNoPerUserCopyInstalls() =>
            Assert.Equal(App.InstallPlan.Install, App.PlanInstall(machine: true, userCopyExists: false, machineCopyExists: true));

        [Fact]
        public void TheAllUsersCopyRemovesThePerUserCopy() =>
            Assert.Equal(App.ConflictRepair.RemoveUserCopy, App.PlanConflictRepair(MachineExe, UserExe, MachineExe, true, true));

        [Fact]
        public void ThePerUserCopyHandsOverToTheAllUsersCopy() =>
            Assert.Equal(App.ConflictRepair.SwitchToAllUsersCopy, App.PlanConflictRepair(UserExe, UserExe, MachineExe, true, true));

        [Theory]
        [InlineData(PortableExe, true, true)]
        [InlineData(UserExe, true, false)]
        [InlineData(MachineExe, false, true)]
        public void NothingIsRepairedWithoutTwoInstallsOrFromAPortableCopy(string current, bool user, bool machine) =>
            Assert.Equal(App.ConflictRepair.None, App.PlanConflictRepair(current, UserExe, MachineExe, user, machine));

        /// <summary>SEC-102: the per-user folder is writable without admin rights, so its exe must
        /// never be started elevated. The hand-over starts the Program Files exe as the same user.</summary>
        [Fact]
        public void TheHandOverStartsTheAllUsersCopyWithoutElevation()
        {
            var start = App.HandOverToAllUsersCopy(MachineExe, 4242, 133000000000000000, []);

            Assert.Equal(MachineExe, start.FileName);
            Assert.Equal("--remove-user-copy 4242 133000000000000000", start.Arguments);
            Assert.True(string.IsNullOrEmpty(start.Verb), start.Verb);
            Assert.Equal(@"C:\Program Files\KillerNotes", start.WorkingDirectory);
        }

        // The per-user copy was started to open a file, take a quick note or start minimized; the all-users copy it
        // hands over to carries that command line on, so the launch still does what it was asked.
        [Fact]
        public void TheHandOverCarriesTheOriginalCommandLine()
        {
            string[] original = [@"C:\Users\x\My Notes\shared.knote", "--quick-note"];
            var start = App.HandOverToAllUsersCopy(MachineExe, 4242, 133000000000000000, original);

            var read = App.HandOverParent(CommandLineToArgs(start.Arguments));

            Assert.NotNull(read);
            Assert.Equal(4242, read!.Parent);
            Assert.Equal(133000000000000000, read.Created);
            Assert.Equal(original, read.Rest);
        }

        [Theory]
        [InlineData("--remove-user-copy 4242 133000000000000000", 4242)]
        [InlineData("--remove-user-copy 4242", null)]                    // no start time: a reused PID could not be told apart
        [InlineData("--remove-user-copy", null)]
        [InlineData("--remove-user-copy x 133000000000000000", null)]
        [InlineData("--remove-user-copy 0 133000000000000000", null)]
        [InlineData("--remove-user-copy 4242 0", null)]
        [InlineData("/remove-machine-conflict", null)]
        public void TheAllUsersCopyReadsWhichCopyHandedOver(string args, int? expected) =>
            Assert.Equal(expected, App.HandOverParent(args.Split(' '))?.Parent);

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern System.IntPtr CommandLineToArgvW(string line, out int count);

        // Reads an argument string back the way Windows hands it to a process's Main.
        private static string[] CommandLineToArgs(string arguments)
        {
            var argv = CommandLineToArgvW("KillerNotes.exe " + arguments, out int count);
            try
            {
                var all = new string[count];
                for (int i = 0; i < count; i++)
                    all[i] = System.Runtime.InteropServices.Marshal.PtrToStringUni(System.Runtime.InteropServices.Marshal.ReadIntPtr(argv, i * System.IntPtr.Size))!;
                return System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(all, 1));
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(argv); }
        }
    }
}
