from dataclasses import dataclass

from Options import DeathLink, PerGameCommonOptions


@dataclass
class BallXPitOptions(PerGameCommonOptions):
    death_link: DeathLink
