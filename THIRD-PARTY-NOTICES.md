# Third-party notices

MultiWiz includes code adapted from the following projects.

## sigil-w101launcher

Parts of the game file downloader (`src/MultiWiz.Core/Patching/GameDownloader.cs` and
`src/MultiWiz.Core/Patching/PatchInfoWriter.cs`: which packages to verify, the dynamic-WAD rule, path
checks, and the `PatchInfo\CRC_<package>.dat` / `LocalPackagesList.txt` bookkeeping) are ported from
sigil-w101launcher's `internal/patch` package (https://github.com/GhostNoodl/sigil-w101launcher).

```
MIT License

Copyright (c) 2026 GhostNoodl

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

The patch protocol framing, the file list (DML table) reader and the KingsIsle CRC-32 in
`src/MultiWiz.Core/Patching` are independent implementations of the formats those projects use; no
code was copied from projects without a license.
