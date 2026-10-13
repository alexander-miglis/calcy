# Binary package built from the prepared staging folder (see build/package.ps1); nothing is compiled here.
%global debug_package %{nil}
%global __os_install_post %{nil}
%global __strip /bin/true
%define _build_id_links none

Name:           calcy
Version:        $(VERSION)
Release:        $(RELEASE)
Summary:        8-bit graphing calculator with exact maths
License:        MIT
URL:            https://github.com/alexander-miglis/calcy
AutoReqProv:    no
Requires:       glibc, libgcc, libstdc++, zlib, libicu, fontconfig, freetype, libX11, libXext, libXrandr, libXi, libXcursor, libICE, libSM
Recommends:     dejavu-sans-fonts

%description
Calcy is a desktop graphing calculator: exact arithmetic, a computer algebra
system, graphing, statistics, matrices, finance, unit conversion, a formula
collection, notebooks and a sandboxed Python interpreter. It also installs
calcy-cli, a terminal calculator and MCP server for AI assistants.

%install
mkdir -p %{buildroot}
cp -a "$(STAGE)/." %{buildroot}/

%files
/opt/calcy
/usr/bin/calcy
/usr/bin/calcy-cli
/usr/share/applications/calcy.desktop
/usr/share/icons/hicolor/*/apps/calcy.png
/usr/share/doc/calcy
%license /usr/share/licenses/calcy/LICENSE