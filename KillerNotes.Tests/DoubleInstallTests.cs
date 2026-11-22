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
            var start = App.HandOverToAllUsersCopy(MachineExe, 4242);

            Assert.Equal(MachineExe, start.FileName);
            Assert.Equal("--remove-user-copy 4242", start.Arguments);
            Assert.True(string.IsNullOrEmpty(start.Verb), start.Verb);
            Assert.Equal(@"C:\Program Files\KillerNotes", start.WorkingDirectory);
        }

        [Theory]
        [InlineData("--remove-user-copy 4242", 4242)]
        [InlineData("--remove-user-copy", null)]
        [InlineData("--remove-user-copy x", null)]
        [InlineData("--remove-user-copy 0", null)]
        [InlineData("/remove-machine-conflict", null)]
        public void TheAllUsersCopyReadsWhichCopyHandedOver(string args, int? expected) =>
            Assert.Equal(expected, App.HandOverParent(args.Split(' ')));
    }
}
