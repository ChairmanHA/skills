# 2026-05-15 fix h2api symlink build dependency

## Problem

On Linux/Raspberry Pi, 3rdParty/h2_api uses a prebuilt versioned shared object and generates libh2api.so symlinks via a custom deploy target. Consumers link against the plain soname path in the runtime lib directory. Build can fail when the link step reaches libh2api.so before the deploy target has created the symlink.

## Local hypothesis

The current ordering is too implicit:

- htra_api_deploy is an ALL custom target
- htra_api is an INTERFACE library with add_dependencies(htra_api htra_api_deploy)
- consumers link to htra_api / HTRA::HTRA and expect libh2api.so to exist

That leaves room for generators/toolchains to schedule the consumer link step before the deploy target result is available.

## Plan

1. Keep the existing library selection and symlink generation logic unchanged.
2. Remove the redundant ALL behavior from htra_api_deploy so it becomes an explicit prerequisite instead of an unrelated default target.
3. Add an explicit dependency from the imported shared library target to htra_api_deploy so consumers of htra_api_imported inherit a concrete build-order edge.
4. Keep htra_api depending on htra_api_deploy as a secondary guard.
5. Run a narrow syntax/error check on 3rdParty/h2_api/CMakeLists.txt after the edit.

## Expected result

Before any target links against libh2api.so in the output lib directory, the deploy script has already copied the versioned shared object and created the required symlink chain.