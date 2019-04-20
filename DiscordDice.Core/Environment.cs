using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordDice
{
    internal static class Environment
    {
        public static bool IsRelease
        {
            get
            {
#if RELEASE
                return true;
#else
                return false;
#endif
            }
        }
    }
}
