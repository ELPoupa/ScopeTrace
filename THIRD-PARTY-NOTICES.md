# Third-party notices

The released `ScopeTrace.exe` is self-contained: it bundles the .NET runtime so it can run without anything installed. These components are included:

| Component | License | Source |
| --- | --- | --- |
| .NET Runtime and Windows Desktop (WPF) | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/wpf |
| System.IO.Ports | MIT | https://github.com/dotnet/runtime |

Both are copyright (c) .NET Foundation and Contributors and released under the MIT license:

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The test tools (xUnit, Microsoft.NET.Test.Sdk, coverlet) are only used to build and test the project and are not shipped in the exe.

Plot labels are drawn using outlines of the Consolas font (or another monospace font) installed with Windows. No font files are distributed with ScopeTrace.

HP and the HP 54600 series are trademarks of their respective owners. ScopeTrace is an independent project and is not affiliated with HP, Agilent or Keysight.
