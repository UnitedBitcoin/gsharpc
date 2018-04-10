gsharpc
=============

C#/.Net compiler for uvm

# Dependencies

* .Net Framework 4.5+(not test on .Net Core 2.0 yet)
* only Windows supported now

# Usage

* compile the gsharpc project and generate gshaprc.exe
* put `gsharp.exe`, `uvm_ass.exe` and `package_gpc.exe` in same directory
* `gsharpc --gpc path-to-contract-project-dll-file` to generate contract file(*.gpc)
* now you can use *.gpc file to register contract in the blockchain
