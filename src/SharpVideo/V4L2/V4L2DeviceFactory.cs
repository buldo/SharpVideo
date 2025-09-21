using System.Runtime.Versioning;
using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public static class V4L2DeviceFactory
{
    public static V4L2Device? Open(string path)
    {
        int deviceFd = Libc.open(path, OpenFlags.O_RDWR);
        if (deviceFd < 0)
        {
            return null;
        }

        var controls = CreateControlsForDevice(deviceFd);

        return new V4L2Device(deviceFd, controls);
    }

    private static List<V4L2DeviceControl> CreateControlsForDevice(int deviceFd)
    {
        var queryCtrl = new V4L2QueryCtrl
        {
            Id = V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL
        };

        var ret = new List<V4L2DeviceControl>();
        while (LibV4L2.QueryControl(deviceFd, ref queryCtrl).Success)
        {
            if (queryCtrl.Flags.HasFlag( V4L2ControlFlags.DISABLED))
            {
                queryCtrl.Id |= V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL;
                continue;
            }

            var controlModel = new V4L2DeviceControl
            {
                Id = queryCtrl.Id,
                DefaultValue = queryCtrl.DefaultValue,
                Flags = queryCtrl.Flags,
                Maximum = queryCtrl.Maximum,
                Minimum = queryCtrl.Minimum,
                Name = queryCtrl.Name,
                Step = queryCtrl.Step,
                Type = queryCtrl.Type
            };
            ret.Add(controlModel);

            //if (queryCtrl.Type == V4L2CtrlType.Menu || queryCtrl.Type == V4L2CtrlType.IntegerMenu)
            //{
            //    Console.WriteLine("      Menu Items:");
            //    var queryMenuItem = new V4L2QueryMenuItem();
            //    for (uint i = (uint)queryCtrl.Minimum; i <= queryCtrl.Maximum; i++)
            //    {
            //        queryMenuItem.Id = queryCtrl.Id;
            //        queryMenuItem.Index = i;
            //        if (LibV4L2.QueryMenuItem(fd, ref queryMenuItem).Success)
            //        {
            //            if (queryCtrl.Type == V4L2CtrlType.Menu)
            //            {
            //                Console.WriteLine($"        {queryMenuItem.Index}: {queryMenuItem.Name}");
            //            }
            //            else // IntegerMenu
            //            {
            //                Console.WriteLine($"        {queryMenuItem.Index}: {queryMenuItem.Value}");
            //            }
            //        }
            //    }
            //}

            queryCtrl.Id |= V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL;
        }

        return ret;
    }
}