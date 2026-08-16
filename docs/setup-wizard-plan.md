# Setup Wizard Requirement

The published Windows setup must be a standalone EXE that opens an interactive WinForms wizard. The expected flow is Welcome -> Options -> Ready -> Installing -> Complete. The installer must keep payload verification and uninstall support, and release CI must verify the wizard contract before publishing.
