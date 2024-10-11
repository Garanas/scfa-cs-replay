using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FAForever.Replay
{
    public record ReplayBodyInvariant(List<ReplayInput> Input, int Tick, int Source, bool InSync, int HashTick, long HashValue, bool EndOfStream);
}
