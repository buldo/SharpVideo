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

    private static unsafe List<V4L2DeviceControl> CreateControlsForDevice(int deviceFd)
    {
        var ret = new List<V4L2DeviceControl>();

        // First try extended control enumeration
        var qext = new V4L2QueryExtCtrl { Id = V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL | V4L2Constants.V4L2_CTRL_FLAG_NEXT_COMPOUND };
        var useExt = LibV4L2.QueryExtendedControl(deviceFd, ref qext).Success;
        if (useExt)
        {
            do
            {
                if (qext.Flags.HasFlag(V4L2ControlFlags.DISABLED))
                {
                    qext.Id |= V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL | V4L2Constants.V4L2_CTRL_FLAG_NEXT_COMPOUND;
                    continue;
                }

                // If menu-type, collect menu entries first
                IReadOnlyList<(uint Index, string? Name, long? Value)>? menuItems = null;
                if (qext.Type == V4L2CtrlType.Menu || qext.Type == V4L2CtrlType.IntegerMenu)
                {
                    var items = new List<(uint Index, string? Name, long? Value)>();
                    var qmi = new V4L2QueryMenuItem { Id = qext.Id };
                    for (uint i = (uint)qext.Minimum; i <= (uint)qext.Maximum; i++)
                    {
                        qmi.Index = i;
                        if (LibV4L2.QueryMenuItem(deviceFd, ref qmi).Success)
                        {
                            if (qext.Type == V4L2CtrlType.Menu)
                                items.Add((i, qmi.Name, null));
                            else
                                items.Add((i, null, qmi.Value));
                        }
                    }
                    menuItems = items.AsReadOnly();
                }

                var control = new V4L2DeviceControl
                {
                    Id = qext.Id,
                    Type = qext.Type,
                    Name = qext.Name,
                    Minimum = qext.Minimum,
                    Maximum = qext.Maximum,
                    Step = qext.Step,
                    DefaultValue = qext.DefaultValue,
                    Flags = qext.Flags,
                    ElemSize = qext.ElemSize,
                    Elems = qext.Elems,
                    NrOfDims = qext.NrOfDims,
                    Dims = new[] { qext.Dims[0], qext.Dims[1], qext.Dims[2], qext.Dims[3] },
                    MenuItems = menuItems
                };

                ret.Add(control);

                qext.Id |= V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL | V4L2Constants.V4L2_CTRL_FLAG_NEXT_COMPOUND;
            } while (LibV4L2.QueryExtendedControl(deviceFd, ref qext).Success);
        }
        else
        {
            // Fallback to legacy queryctrl enumeration
            var queryCtrl = new V4L2QueryCtrl { Id = V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL };
            while (LibV4L2.QueryControl(deviceFd, ref queryCtrl).Success)
            {
                if (queryCtrl.Flags.HasFlag(V4L2ControlFlags.DISABLED))
                {
                    queryCtrl.Id |= V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL;
                    continue;
                }

                var menuItems = default(List<(uint, string?, long?)>);
                if (queryCtrl.Type == V4L2CtrlType.Menu || queryCtrl.Type == V4L2CtrlType.IntegerMenu)
                {
                    menuItems = new List<(uint, string?, long?)>();
                    var qmi = new V4L2QueryMenuItem { Id = queryCtrl.Id };
                    for (uint i = (uint)queryCtrl.Minimum; i <= (uint)queryCtrl.Maximum; i++)
                    {
                        qmi.Index = i;
                        if (LibV4L2.QueryMenuItem(deviceFd, ref qmi).Success)
                        {
                            if (queryCtrl.Type == V4L2CtrlType.Menu)
                                menuItems.Add((i, qmi.Name, null));
                            else
                                menuItems.Add((i, null, qmi.Value));
                        }
                    }
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
                    Type = queryCtrl.Type,
                    MenuItems = menuItems?.AsReadOnly()
                };
                ret.Add(controlModel);

                queryCtrl.Id |= V4L2Constants.V4L2_CTRL_FLAG_NEXT_CTRL;
            }
        }

        return ret;
    }
}