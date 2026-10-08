using System.IO;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core;

namespace JobTracker.App.ViewModels;

/// <summary>A starred bullet in the library tab.</summary>
public sealed partial class BulletVM(StarredBullet bullet, MainViewModel main)
{
    public string Text => bullet.Text;

    public string Origin =>
        $"{bullet.Company} · {bullet.Role} · {(bullet.Source == BulletSource.Jd ? "JD" : "Resume")}";

    [RelayCommand]
    private void Copy() => main.Copy(bullet.Text);

    [RelayCommand]
    private void Unstar()
    {
        main.Try(() => main.BulletLibrary.Remove(bullet.Id));
        main.RefreshBullets();
        main.ShowToast("Removed from starred bullets");
    }

    [RelayCommand]
    private void OpenApplication()
    {
        main.SelectByFolder(Path.Combine(main.Store.Root, bullet.AppFolder));
        main.SelectedTabIndex = 0;
    }
}
