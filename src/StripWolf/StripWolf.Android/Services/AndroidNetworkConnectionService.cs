using Android.App;
using Android.Content;
using Android.Net;
using StripWolf.Core.Services;

namespace StripWolf.Core.Android.Services;

public class AndroidNetworkConnectionService : INetworkConnectionService
{
    public bool IsConnectionMetered()
    {
        var connectivityManager = Application.Context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        if (connectivityManager is null)
        {
            return false;
        }

        return connectivityManager.IsActiveNetworkMetered;
    }
}
