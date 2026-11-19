# Unit tests of the pure helpers of the deploy module. Run: pwsh -NoProfile -c "Invoke-Pester tests/deploy -Output Detailed"

BeforeAll {
    Import-Module "$PSScriptRoot/../../deploy/lib/EphemeralEnvs.Deploy.psm1" -Force
}

Describe 'Get-EnvName -Branch' {
    It 'lowercases and replaces every non [a-z0-9] run with one dash' {
        Get-EnvName -Branch 'feature/Add-X_2' | Should -Be 'feature-add-x-2'
    }

    It 'trims leading and trailing dashes' {
        Get-EnvName -Branch '--Fix//bug--' | Should -Be 'fix-bug'
    }

    It 'keeps a name that is already normal' {
        Get-EnvName -Branch 'master' | Should -Be 'master'
    }

    It 'shortens a long name to 40 chars: 33-char head + dash + 6 hex' {
        $name = Get-EnvName -Branch ('a' * 50)
        $name.Length | Should -Be 40
        $name | Should -Match '^a{33}-[0-9a-f]{6}$'
    }

    It 'gives the same short name for the same branch' {
        Get-EnvName -Branch ('a' * 50) | Should -Be (Get-EnvName -Branch ('a' * 50))
    }

    It 'gives different short names to long branches that differ only at the end' {
        Get-EnvName -Branch (('a' * 50) + 'x') | Should -Not -Be (Get-EnvName -Branch (('a' * 50) + 'y'))
    }

    It 'trims a trailing dash of the shortened head' {
        $name = Get-EnvName -Branch (('a' * 32) + ('-b' * 10))
        $name | Should -Match '^a{32}-[0-9a-f]{6}$'
    }

    It 'keeps a name of exactly 40 chars' {
        Get-EnvName -Branch ('a' * 40) | Should -Be ('a' * 40)
    }

    It 'rejects a branch that normalizes to an empty name' {
        { Get-EnvName -Branch '///' } | Should -Throw '*empty*'
    }

    It 'rejects the reserved name platform' {
        { Get-EnvName -Branch 'platform' } | Should -Throw '*reserved*'
    }
}

Describe 'Get-EnvName -Env' {
    It 'accepts a name in normal form' {
        Get-EnvName -Env 'feature-x' | Should -Be 'feature-x'
    }

    It 'rejects a name that is not in normal form and suggests the normal one' {
        { Get-EnvName -Env 'Feature/X' } | Should -Throw "*use 'feature-x'*"
    }

    It 'rejects the reserved name platform' {
        { Get-EnvName -Env 'platform' } | Should -Throw '*reserved*'
    }
}

Describe 'ConvertTo-DbName' {
    It 'prefixes ee_ and replaces dashes with underscores' {
        ConvertTo-DbName -EnvName 'feature-x' | Should -Be 'ee_feature_x'
    }
}

Describe 'New-DbPassword' {
    It 'returns 32 lowercase hex chars' {
        New-DbPassword | Should -Match '^[0-9a-f]{32}$'
    }

    It 'returns a new password every time' {
        New-DbPassword | Should -Not -Be (New-DbPassword)
    }
}
