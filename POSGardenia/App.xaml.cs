using POSGardenia.Data;
using System.Configuration;
using System.Data;
using System.Windows;

namespace POSGardenia
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DatabaseHelper.InitializeDatabase();
        }
    }
}
