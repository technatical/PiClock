#!/bin/bash
# Runs mpv with the user's PipeWire session visible, even when launched from the
# piclock systemd service (which has no user environment).
# All arguments are passed straight through to mpv (offset, fade filter, file).
export XDG_RUNTIME_DIR=/run/user/1000
exec mpv --no-video --no-terminal "$@"
