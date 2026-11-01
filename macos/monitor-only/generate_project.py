from pathlib import Path
import hashlib
root=Path(__file__).parent
project=root/'GPTDeskTopMonitor.xcodeproj'
project.mkdir(exist_ok=True)
sources=['MonitorApp.swift','MonitorModels.swift','MonitorStore.swift','ChromeCDP.swift','MonitorEngine.swift']

def oid(name): return hashlib.sha256(name.encode()).hexdigest().upper()[:24]
lines=[]
def a(s): lines.append(s)
a('// !$*UTF8*$!')
a('{ archiveVersion = 1; classes = {}; objectVersion = 56; objects = {')
for name in sources:
    a(f'{oid("build"+name)} /* {name} in Sources */ = {{isa = PBXBuildFile; fileRef = {oid("file"+name)} /* {name} */; }};')
for name in sources:
    a(f'{oid("file"+name)} /* {name} */ = {{isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = {name}; sourceTree = "<group>"; }};')
a(f'{oid("info")} /* Info.plist */ = {{isa = PBXFileReference; lastKnownFileType = text.plist.xml; path = Info.plist; sourceTree = "<group>"; }};')
a(f'{oid("appicon")} /* AppIcon.icns */ = {{isa = PBXFileReference; lastKnownFileType = image.icns; path = AppIcon.icns; sourceTree = "<group>"; }};')
a(f'{oid("buildappicon")} /* AppIcon.icns in Resources */ = {{isa = PBXBuildFile; fileRef = {oid("appicon")} /* AppIcon.icns */; }};')
a(f'{oid("productfile")} /* GPTDeskTop Monitor Only.app */ = {{isa = PBXFileReference; explicitFileType = wrapper.application; includeInIndex = 0; path = "GPTDeskTop Monitor Only.app"; sourceTree = BUILT_PRODUCTS_DIR; }};')
a(f'{oid("frameworks")} /* Frameworks */ = {{isa = PBXFrameworksBuildPhase; buildActionMask = 2147483647; files = (); runOnlyForDeploymentPostprocessing = 0; }};')
a(f'{oid("sources")} /* Sources */ = {{isa = PBXSourcesBuildPhase; buildActionMask = 2147483647; files = (')
for name in sources:a(f'{oid("build"+name)} /* {name} in Sources */,')
a('); runOnlyForDeploymentPostprocessing = 0; };')
a(f'{oid("resources")} /* Resources */ = {{isa = PBXResourcesBuildPhase; buildActionMask = 2147483647; files = ({oid("buildappicon")} /* AppIcon.icns in Resources */,); runOnlyForDeploymentPostprocessing = 0; }};')
a(f'{oid("groupmain")} = {{isa = PBXGroup; children = ({oid("groupapp")}, {oid("groupproduct")}); sourceTree = "<group>"; }};')
a(f'{oid("groupapp")} = {{isa = PBXGroup; children = ({oid("groupsources")}, {oid("groupresources")}); path = GPTDeskTopMonitor; sourceTree = "<group>"; }};')
a(f'{oid("groupsources")} = {{isa = PBXGroup; children = (')
for name in sources:a(f'{oid("file"+name)},')
a('); path = Sources; sourceTree = "<group>"; };')
a(f'{oid("groupresources")} = {{isa = PBXGroup; children = ({oid("info")}, {oid("appicon")}); path = Resources; sourceTree = "<group>"; }};')
a(f'{oid("groupproduct")} = {{isa = PBXGroup; children = ({oid("productfile")}); name = Products; sourceTree = "<group>"; }};')
a(f'{oid("target")} = {{isa = PBXNativeTarget; buildConfigurationList = {oid("targetconfigs")}; buildPhases = ({oid("sources")}, {oid("frameworks")}, {oid("resources")}); buildRules = (); dependencies = (); name = "GPTDeskTop Monitor Only"; productName = "GPTDeskTop Monitor Only"; productReference = {oid("productfile")}; productType = "com.apple.product-type.application"; }};')
a(f'{oid("project")} = {{isa = PBXProject; attributes = {{ LastUpgradeCheck = 1600; TargetAttributes = {{ {oid("target")} = {{ CreatedOnToolsVersion = 16.0; }}; }}; }}; buildConfigurationList = {oid("projectconfigs")}; compatibilityVersion = "Xcode 15.0"; developmentRegion = en; hasScannedForEncodings = 0; knownRegions = (en, Base); mainGroup = {oid("groupmain")}; productRefGroup = {oid("groupproduct")}; projectDirPath = ""; projectRoot = ""; targets = ({oid("target")}); }};')
projectsettings='SDKROOT = macosx; MACOSX_DEPLOYMENT_TARGET = 13.0; CLANG_ENABLE_MODULES = YES; SWIFT_VERSION = 5.0;'
for mode in ['Debug','Release']:
 a(f'{oid("project"+mode)} = {{isa = XCBuildConfiguration; buildSettings = {{{projectsettings} }}; name = {mode}; }};')
 targetsettings=f'''ASSETCATALOG_COMPILER_APPICON_NAME = ""; CODE_SIGN_STYLE = Automatic; CURRENT_PROJECT_VERSION = 1; GENERATE_INFOPLIST_FILE = NO; INFOPLIST_FILE = GPTDeskTopMonitor/Resources/Info.plist; LD_RUNPATH_SEARCH_PATHS = ("$(inherited)", "@executable_path/../Frameworks"); MACOSX_DEPLOYMENT_TARGET = 13.0; PRODUCT_BUNDLE_IDENTIFIER = com.walid.gptdesktop.monitoronly; PRODUCT_NAME = "GPTDeskTop Monitor Only"; SWIFT_VERSION = 5.0; TARGETED_DEVICE_FAMILY = 1; SWIFT_EMIT_LOC_STRINGS = YES;'''
 a(f'{oid("target"+mode)} = {{isa = XCBuildConfiguration; buildSettings = {{{targetsettings} }}; name = {mode}; }};')
a(f'{oid("projectconfigs")} = {{isa = XCConfigurationList; buildConfigurations = ({oid("projectDebug")}, {oid("projectRelease")}); defaultConfigurationIsVisible = 0; defaultConfigurationName = Release; }};')
a(f'{oid("targetconfigs")} = {{isa = XCConfigurationList; buildConfigurations = ({oid("targetDebug")}, {oid("targetRelease")}); defaultConfigurationIsVisible = 0; defaultConfigurationName = Release; }};')
a(f'}}; rootObject = {oid("project")}; }}')
(project/'project.pbxproj').write_text('\n'.join(lines)+'\n')
print(project/'project.pbxproj')
