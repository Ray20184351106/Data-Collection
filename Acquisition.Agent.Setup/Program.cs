using System.ComponentModel;

namespace Acquisition.Agent.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var packageRoot = Path.GetFullPath(AppContext.BaseDirectory);
            var deploymentPath = Path.Combine(packageRoot, "deployment.json");
            var payloadManifestPath = Path.Combine(packageRoot, "payload.manifest.json");
            var loader = new ManifestFileLoader();
            var deployment = loader.LoadDeployment(deploymentPath);
            var payload = loader.LoadPayload(payloadManifestPath);

            var validation = new DeploymentManifestValidator(new SystemDriveTypeProvider()).Validate(deployment);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));
            var validated = validation.Value!;

            var payloadValidation = new PayloadIntegrityVerifier().Verify(packageRoot, payload);
            if (!payloadValidation.IsValid) throw new InvalidDataException(string.Join(Environment.NewLine, payloadValidation.Errors));
            ValidateTargetFiles(validated);

            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定AgentSetup.exe路径。");
            var isElevatedLaunch = args.Any(argument => string.Equals(argument, "--elevated", StringComparison.Ordinal));
            if (!WindowsElevation.IsAdministrator())
            {
                if (isElevatedLaunch) throw new InvalidOperationException("提升后的安装器仍没有管理员权限。");
                WindowsElevation.Relaunch(ElevationRequestFactory.Create(executablePath, packageRoot));
                return;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var plan = new InstallationPlanner(programFiles, programData).Create(packageRoot, validated, payload);
            var runner = new WindowsProcessRunner();
            var services = new ScServiceManager(new ServiceCommandFactory(windowsDirectory), runner);
            var accessControl = new IcaclsFileAccessControlManager(new AccessControlCommandFactory(windowsDirectory), runner);
            var executor = new InstallationExecutor(
                new SystemInstallerFileSystem(),
                new SystemMachineEnvironmentStore(),
                services,
                accessControl);
            Application.Run(new SetupForm(plan, executor));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            MessageBox.Show("管理员授权已取消，未执行安装。", "Agent安装", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "安装包校验失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ValidateTargetFiles(ValidatedDeployment deployment)
    {
        var errors = new List<string>();
        if (!Directory.Exists(deployment.AcquisitionAppDirectory)) errors.Add("采集程序目录不存在。");
        if (!File.Exists(deployment.AcquisitionExecutablePath)) errors.Add("采集程序目录中缺少文件数据采集系统.exe。");
        if (!File.Exists(deployment.LegacyDatabasePath)) errors.Add("采集程序目录中缺少Data\\采集记录.db。");
        if (!File.Exists(deployment.LegacyConfigPath)) errors.Add("采集程序目录中缺少config.json。");
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }
}
