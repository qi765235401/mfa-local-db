using MfaLocalDb.Services;

namespace MfaLocalDb;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppResources.Prepare();

        var database = new DatabaseService(AppResources.DatabasePath);
        database.Initialize();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(database));
    }
}
