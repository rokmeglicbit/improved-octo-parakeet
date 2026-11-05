using UIKit;
namespace SkiaBgRepro;
public class Program
{
    static void Main(string[] args)
    {
        // if you want to use a different Application Delegate class from "AppDelegate" you can specify it here.
        // Znanje - https://github.com/dotnet/maui/issues/23682#issuecomment-2271906346
        try
        {
            UIApplication.Main(args, null, typeof(AppDelegate));
        }
        catch (Exception ex)
        {
            Console.WriteLine("iOS.Program", ex);
        }
    }
}
