# infinite-splitter — build, test and packaging entry points.
#
# `make` alone lists the targets. `make all` is the full gate the release runs:
# offline suite, compile, Components/, versioned zip.
#
# Requires nothing but the .NET SDK. See docs/development.md.

SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c

ROOT := $(patsubst %/,%,$(dir $(abspath $(lastword $(MAKEFILE_LIST)))))

export PATH := $(HOME)/.dotnet:$(PATH)
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT := 1
export DOTNET_CLI_TELEMETRY_OPTOUT := 1
export DOTNET_NOLOGO := 1

CONFIG        ?= Release
LIVESPLIT_DIR ?= /mnt/d/speedrun/livesplit/LiveSplit_1.8.37
PUBLISH_DIR   ?= /mnt/c/Users/$(USER)/AppData/Local/Temp/splitcheck

COMPONENT  := $(ROOT)/src/InfiniteSplitter
SPLITCHECK := $(ROOT)/tools/splitcheck
BIN        := $(COMPONENT)/bin/$(CONFIG)
COMPONENTS := $(ROOT)/Components
DIST       := $(ROOT)/dist

SHIPPED := InfiniteSplitter.dll

.PHONY: help all test build components version-check release publish publish-dry deploy splitcheck clean

help:
	@echo "infinite-splitter"
	@echo
	@echo "  make test           offline suite — no game, no LiveSplit, runs on Linux"
	@echo "  make build          compile the component"
	@echo "  make components     compile and refresh Components/ (what a user installs)"
	@echo "  make release        components + version check + dist/InfiniteSplitter-<v>.zip"
	@echo "  make all            test + release (the full gate)"
	@echo
	@echo "  make publish        build, commit, tag, push, open a DRAFT release on GitHub"
	@echo "  make publish-dry    build and package only — no commit, no tag, no push"
	@echo "  make version-check  csproj <Version> must match Components/Updates.xml"
	@echo "  make deploy         copy Components/ into LiveSplit (LIVESPLIT_DIR=$(LIVESPLIT_DIR))"
	@echo "  make splitcheck     publish the live attach tool for win-x64 (PUBLISH_DIR)"
	@echo "  make clean          remove bin/, obj/ and dist/"

all: test release

test:
	@echo ">> selftest"
	@dotnet run -c $(CONFIG) --project $(SPLITCHECK) -- --selftest | sed 's/^/   /'

build:
	@echo ">> build"
	@dotnet build -c $(CONFIG) $(COMPONENT) \
	  | sed -E '/warning MSB3277|Project file item includes|caused reference/d'

components: build
	@echo ">> Components/"
	@mkdir -p $(COMPONENTS)
	@for dll in $(SHIPPED); do cp "$(BIN)/$$dll" "$(COMPONENTS)/"; done
	@sed 's/$$/\r/' $(ROOT)/tools/dist-readme.txt > $(COMPONENTS)/readme.txt
	@cd $(COMPONENTS) && find . -type f | sed 's|^\./|   |' | sort

version-check:
	@csproj=$$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' $(COMPONENT)/InfiniteSplitter.csproj | head -1); \
	manifest=$$(sed -n 's:.*<update version="\([^"]*\)".*:\1:p' $(COMPONENTS)/Updates.xml | head -1); \
	if [ "$$csproj" != "$$manifest" ]; then \
	  echo ">> ERROR: version mismatch — csproj says '$$csproj', Components/Updates.xml says '$$manifest'."; \
	  echo ">>        Add an <update version=\"$$csproj\"> entry at the top of Updates.xml."; \
	  exit 1; \
	fi; \
	echo ">> version $$csproj (csproj and Updates.xml agree)"

release: components version-check
	@echo ">> release zip"
	@mkdir -p $(DIST)
	@version=$$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' $(COMPONENT)/InfiniteSplitter.csproj | head -1); \
	python3 $(ROOT)/tools/package.py "$(DIST)/InfiniteSplitter-$$version.zip" "$(COMPONENTS)"

publish:
	@$(ROOT)/tools/publish.sh

publish-dry:
	@$(ROOT)/tools/publish.sh --dry-run

deploy: components
	@LIVESPLIT_DIR="$(LIVESPLIT_DIR)" $(ROOT)/deploy.sh

splitcheck:
	@echo ">> publishing splitcheck to $(PUBLISH_DIR)"
	@dotnet publish -c $(CONFIG) -r win-x64 --self-contained false -o "$(PUBLISH_DIR)" $(SPLITCHECK)

clean:
	@rm -rf $(DIST)
	@find $(ROOT)/src $(ROOT)/tools -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
	@echo ">> cleaned"
